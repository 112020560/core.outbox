# SmartCore.Outbox — Architecture & Design Document

## Overview

`SmartCore.Outbox` is a NuGet package that implements the **Transactional Outbox + Event Store** pattern for .NET microservices. It decouples event persistence from event delivery: services write events to a dedicated database, and a standalone worker process reads and routes them to the message broker.

---

## Problem It Solves

- Events published directly to RabbitMQ are lost if the broker is down or the app crashes between DB commit and publish.
- No full event history when there are no active consumers.
- No idempotency guarantee for consumers receiving redelivered messages.

---

## Two Components

### 1. `SmartCore.Outbox` (NuGet Package)

Installed in every microservice. Responsible **only for writing** events to the shared outbox database. It carries no processing logic.

### 2. `outbox-worker` (Standalone Service)

A single process deployed in the infrastructure. Reads from the shared outbox database and routes events to RabbitMQ (or other destinations) using a configurable factory. Only one instance of this service exists in the system.

---

## Architecture Diagram

```
  CRM Service                Billing Service           Inventory Service
  [NuGet installed]          [NuGet installed]         [NuGet installed]
       |                           |                          |
       |  IOutboxWriter            |  IOutboxWriter           |  IOutboxWriter
       |  .AppendAsync()           |  .AppendAsync()          |  .AppendAsync()
       +---------------------------+--------------------------+
                                   |
                                   v
                        [ outbox_db — PostgreSQL ]
                            Events table
                                   |
                                   v
                         [ outbox-worker ]
                       (single process in infra)
                                   |
                        IPublisherFactory by EventType
                        |-- CustomerCreated  --> RabbitMQ exchange "crm.events"
                        |-- InvoiceCreated   --> RabbitMQ exchange "billing.events"
                        +-- StockUpdated     --> Skipped (no consumer yet, event preserved)
```

---

## Database Design (Option B — Dedicated `outbox_db`)

All services write to a single dedicated database. This database is managed exclusively by `SmartCore.Outbox` and the `outbox-worker`. It never shares a connection with any service's domain database.

### `Events` Table

```sql
CREATE TABLE Events (
    Id                UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    ServiceName       VARCHAR(128) NOT NULL,        -- "crm", "billing", "inventory"
    DeduplicationKey  VARCHAR(256) UNIQUE,           -- prevents duplicate inserts
    AggregateId       UUID         NOT NULL,
    AggregateType     VARCHAR(128),
    EventType         VARCHAR(256) NOT NULL,
    Payload           JSONB        NOT NULL,
    OccurredAt        TIMESTAMPTZ  NOT NULL,
    Status            VARCHAR(32)  NOT NULL DEFAULT 'Pending',
    ClaimedBy         VARCHAR(128),                  -- worker instance ID
    ClaimedAt         TIMESTAMPTZ,
    PublishedAt       TIMESTAMPTZ,
    RetryCount        INT          NOT NULL DEFAULT 0,
    LastError         TEXT
);

-- Status values: Pending | Published | Skipped | Failed
-- Index for worker polling
CREATE INDEX idx_events_status_claimedat ON Events (Status, ClaimedAt)
    WHERE Status = 'Pending';
```

### `ProcessedEvents` Table

Used by consumers to guarantee idempotency on the receiving end. Managed by the `outbox-worker` but the helper is exposed via the NuGet for consumers to use.

```sql
CREATE TABLE ProcessedEvents (
    EventId       UUID         NOT NULL,
    ConsumerName  VARCHAR(256) NOT NULL,
    ProcessedAt   TIMESTAMPTZ  NOT NULL,
    PRIMARY KEY (EventId, ConsumerName)
);
```

---

## NuGet Package: `SmartCore.Outbox`

### Solution Structure

```
SmartCore.Outbox/
├── Src/
│   └── SmartCore.Outbox/
│       ├── Abstractions/
│       │   ├── IOutboxWriter.cs
│       │   └── IIdempotencyGuard.cs
│       ├── Models/
│       │   └── OutboxEvent.cs
│       ├── Infrastructure/
│       │   ├── OutboxDbContext.cs          -- own DbContext, no overlap with consumer app
│       │   ├── OutboxRepository.cs
│       │   └── Migrations/                 -- embedded, auto-applied on startup
│       └── Extensions/
│           └── ServiceCollectionExtensions.cs
└── Tests/
    ├── SmartCore.Outbox.UnitTests/
    └── SmartCore.Outbox.IntegrationTests/  -- Testcontainers (real Postgres)
```

### Key Abstractions

```csharp
// What microservices call from their CommandHandlers
public interface IOutboxWriter
{
    Task AppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default);
    Task<bool> TryAppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default);
}

// Exposed for consumers to check before processing
public interface IIdempotencyGuard
{
    Task<bool> AlreadyProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);
    Task MarkAsProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);
}

// Event model
public class OutboxEvent
{
    public Guid   Id               { get; init; } = Guid.CreateVersion7();
    public string ServiceName      { get; init; } = string.Empty;
    public string DeduplicationKey { get; init; } = string.Empty;
    public Guid   AggregateId      { get; init; }
    public string AggregateType    { get; init; } = string.Empty;
    public string EventType        { get; init; } = string.Empty;
    public string Payload          { get; init; } = string.Empty;  // JSON serialized
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
```

### Registration in Consuming Service

```csharp
// Program.cs of any microservice
builder.Services.AddSmartOutbox(options =>
{
    options.ConnectionString = builder.Configuration["Outbox:ConnectionString"];
    options.ServiceName      = "crm";
});
```

### Usage in a CommandHandler

```csharp
public async Task<Result<Guid>> Handle(CreateCustomerCommand command, CancellationToken ct)
{
    // 1. Domain logic + save to own DB
    var customer = Customer.Create(command.Dto);
    await _unitOfWork.Customers.AddAsync(customer, ct);
    await _unitOfWork.SaveChangesAsync(ct);

    // 2. Write event to outbox (separate DB, non-transactional with domain DB)
    await _outboxWriter.AppendAsync(new OutboxEvent
    {
        ServiceName      = "crm",
        AggregateId      = customer.Id,
        AggregateType    = "Customer",
        EventType        = "CustomerCreated",
        DeduplicationKey = $"CustomerCreated:{customer.Id}",
        Payload          = JsonSerializer.Serialize(new CustomerCreatedPayload(customer))
    }, ct);

    return Result.Success(customer.Id);
}
```

### Transactionality Note

Because the NuGet writes to a separate `outbox_db`, the outbox write is **not in the same transaction** as the domain `SaveChanges()`. The mitigation is:

- Domain commit happens first (source of truth is always the domain DB).
- If the outbox write fails, it is logged as a warning. The event can be reinserted manually or via aggregate replay.
- `TryAppendAsync` returns `false` on failure without throwing, giving the caller control.
- For most use cases, `AppendAsync` with fire-and-forget semantics is sufficient.

---

## Standalone Service: `outbox-worker`

### Solution Structure

```
outbox-worker/
├── Src/
│   └── OutboxWorker/
│       ├── Program.cs
│       ├── OutboxProcessor.cs          -- BackgroundService, main loop
│       ├── ClaimManager.cs             -- claim-based distributed locking
│       ├── Publishers/
│       │   ├── IEventPublisher.cs
│       │   ├── IPublisherFactory.cs
│       │   └── RabbitMq/
│       │       ├── RabbitMqPublisher.cs
│       │       └── RabbitMqPublisherOptions.cs
│       └── appsettings.json
└── Tests/
    └── OutboxWorker.IntegrationTests/
```

### Configuration

```json
{
  "Outbox": {
    "ConnectionString": "Host=...;Database=outbox_db;",
    "PollingIntervalSeconds": 5,
    "BatchSize": 50,
    "ClaimTimeoutSeconds": 60,
    "MaxRetries": 5
  },
  "RabbitMq": {
    "Uri": "amqp://user:pass@localhost:5672"
  },
  "Publishers": {
    "CustomerCreated": {
      "Exchange": "crm.events",
      "RoutingKey": "customer.created"
    },
    "CustomerConverted": {
      "Exchange": "crm.events",
      "RoutingKey": "customer.converted"
    },
    "InvoiceCreated": {
      "Exchange": "billing.events",
      "RoutingKey": "invoice.created"
    }
  }
}
```

### OutboxProcessor Loop

```
Every N seconds:
  1. CLAIM BATCH
     UPDATE Events
     SET ClaimedBy = {instanceId}, ClaimedAt = now()
     WHERE Status = 'Pending'
       AND (ClaimedAt IS NULL OR ClaimedAt < now() - interval '{ClaimTimeout}')
     LIMIT {BatchSize}
     RETURNING *

  2. FOR EACH claimed event:
     a. factory.GetPublisher(eventType) -> IEventPublisher?
        - Publisher found  -> publish to exchange
                           -> UPDATE Status='Published', PublishedAt=now()
        - Not found        -> UPDATE Status='Skipped'
                              (event stays in history, no consumer configured yet)
        - Publish error    -> RetryCount++, ClaimedBy=NULL, ClaimedAt=NULL
                              if RetryCount >= MaxRetries -> Status='Failed'

  3. RELEASE STALE CLAIMS
     UPDATE Events
     SET ClaimedBy=NULL, ClaimedAt=NULL
     WHERE Status='Pending'
       AND ClaimedAt < now() - interval '{ClaimTimeout}'
     (handles worker crash mid-batch)
```

### Idempotency in Consumers (RabbitMQ side)

Any consumer receiving events from Rabbit uses `IIdempotencyGuard` (also available via the NuGet) before processing:

```csharp
// In a MassTransit consumer or custom consumer
public async Task Consume(ConsumeContext<CustomerCreatedEvent> context)
{
    var eventId = context.Message.EventId;

    if (await _idempotencyGuard.AlreadyProcessedAsync(eventId, "EmailNotificationConsumer"))
        return;

    await _emailService.SendWelcomeEmailAsync(context.Message);

    await _idempotencyGuard.MarkAsProcessedAsync(eventId, "EmailNotificationConsumer");
}
```

---

## Idempotency Summary

| Level | Mechanism | Guarantees |
|---|---|---|
| Producer (write) | `DeduplicationKey` UNIQUE constraint | One event row per logical event, even if command retries |
| Worker (publish) | Claim-based lock + Status column | One publish attempt at a time per event across N worker restarts |
| Consumer (process) | `ProcessedEvents` table | No double processing on Rabbit redelivery |

---

## Recommended Build Order

```
Phase 1 — NuGet Package
  1. OutboxEvent model
  2. IOutboxWriter interface
  3. OutboxDbContext (own context, embedded migrations)
  4. OutboxRepository
  5. IIdempotencyGuard + implementation
  6. ServiceCollectionExtensions (.AddSmartOutbox)
  7. Unit + integration tests (Testcontainers)
  8. Pack and publish to internal NuGet feed

Phase 2 — Outbox Worker
  1. IEventPublisher + IPublisherFactory
  2. RabbitMqPublisher
  3. ClaimManager
  4. OutboxProcessor (BackgroundService)
  5. Program.cs wiring
  6. Integration tests
  7. Dockerize + deploy

Phase 3 — Migrate Existing Services
  1. Install NuGet in CRM service
  2. Replace IMqProducerService.PublishEvent() calls with IOutboxWriter.AppendAsync()
  3. Configure Publishers in outbox-worker for CRM event types
  4. Validate end-to-end
  5. Repeat for other services
```

---

## Repositories

| Repo | Purpose |
|---|---|
| `smartcore-outbox` | NuGet package source (`SmartCore.Outbox`) |
| `outbox-worker` | Standalone worker service |

---

## Technology Stack

- **.NET 9** — Class Library + Worker Service
- **EF Core 9 + Npgsql** — ORM for `outbox_db`
- **MassTransit** — RabbitMQ publisher (reuses existing pattern from CRM)
- **Testcontainers** — integration tests with real PostgreSQL
- **GitHub Actions** — CI + NuGet pack + publish to GitHub Packages or internal feed
