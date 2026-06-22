## Why

Microservices in SmartCore publish domain events directly to RabbitMQ, creating a reliability gap: events are lost if the broker is unavailable or the process crashes between a database commit and the publish call. A dedicated NuGet package implementing the Transactional Outbox pattern eliminates this gap by durably persisting events to a dedicated PostgreSQL database before any delivery attempt.

## What Changes

- New NuGet package `SmartCore.Outbox` (Class Library, .NET 9) added to `SmartCoreOutbox.sln`
- Introduces `IOutboxWriter` for appending events from any microservice command handler
- Introduces `IIdempotencyGuard` for consumers to prevent double-processing of redelivered messages
- Introduces `OutboxEvent` immutable record as the canonical event model
- Introduces `OutboxDbContext` with its own EF Core migrations targeting a dedicated `outbox_db` (PostgreSQL) — no overlap with any service's domain database
- `AddSmartOutbox(options)` extension method for one-line registration in consuming services
- Unit and integration test projects (Testcontainers, real PostgreSQL) added to the solution

## Capabilities

### New Capabilities

- `outbox-writer`: Append domain events to the outbox database via `IOutboxWriter.AppendAsync` and `TryAppendAsync`. Includes `DeduplicationKey` UNIQUE constraint to prevent duplicate inserts on command retries.
- `idempotency-guard`: Allow RabbitMQ consumers to check and record event processing state via `IIdempotencyGuard`, backed by the `ProcessedEvents` table in `outbox_db`.
- `outbox-db-schema`: Managed EF Core schema for `outbox_db`: `Events` table (with status, claim, retry columns) and `ProcessedEvents` table. Migrations are embedded in the package and auto-applied on host startup.
- `service-registration`: `AddSmartOutbox(OutboxOptions)` extension on `IServiceCollection` to register all dependencies (DbContext, writer, idempotency guard) with a single call.

### Modified Capabilities

*(none — this is a new package with no existing specs)*

## Impact

- New projects added to `SmartCoreOutbox.sln`: `SmartCore.Outbox`, `SmartCore.Outbox.UnitTests`, `SmartCore.Outbox.IntegrationTests`
- Consuming microservices add `SmartCore.Outbox` NuGet reference and call `AddSmartOutbox()` in `Program.cs`
- Requires access to a dedicated PostgreSQL `outbox_db` instance (connection string per service via config)
- No dependency on `SharedKernel` — payload is stored as raw JSON string; the caller serializes
- No changes to any existing domain DbContext or domain database

## Non-goals

- Event delivery or routing to RabbitMQ (handled by `outbox-worker`, a separate component)
- Deserialization or interpretation of event payloads
- Multi-tenant or per-service database isolation within `outbox_db`
- Distributed tracing or OpenTelemetry integration (future concern)
