## Context

`SmartCore.Outbox` (Phase 1) persists events to `outbox_db` but never delivers them. The `outbox-worker` is the single infrastructure process responsible for polling that database, claiming batches of pending events, and routing each to the correct RabbitMQ exchange. It runs as a standalone Docker container deployed alongside the platform's infrastructure services, not inside any microservice.

The worker reads `outbox_db` using the same `OutboxDbContext` provided by the `SmartCore.Outbox` NuGet, and maps `EventType` strings to RabbitMQ routes via configuration-driven `IPublisherFactory`.

## Goals / Non-Goals

**Goals:**
- Deliver events from `outbox_db` to RabbitMQ with at-least-once semantics
- Support per-EventType routing to different exchanges and routing keys via `appsettings.json`
- Handle `Published`, `Skipped` (no route configured), and `Failed` (max retries exceeded) outcomes
- Recover stale claims from crashed worker instances automatically
- Be deployable as a Docker container with zero-touch configuration

**Non-Goals:**
- Horizontal scaling (single instance design; claim-based locking is defensive, not a scale-out strategy)
- Writing to `outbox_db` (responsibility of `SmartCore.Outbox` NuGet)
- Non-RabbitMQ publishers in this phase (architecture allows extension via `IEventPublisher`)
- Dead-letter queue management or replay UI
- Deserialization of event payloads for routing — routing is purely by `EventType` string

## Decisions

### Decision 1: Claim-based distributed locking (not PostgreSQL advisory locks)

**Choice**: Use `UPDATE Events SET ClaimedBy = {instanceId}, ClaimedAt = now() WHERE Status = 'Pending' AND (ClaimedAt IS NULL OR ClaimedAt < now() - interval '{timeout}') LIMIT {batch} RETURNING *` for atomic batch claiming.

**Rationale**: Advisory locks require a persistent connection per lock, which is fragile under connection pooling. Column-based claims are durable (survive connection drops), inspectable, and self-healing (stale claim recovery by time comparison). This matches the architecture doc's `ClaimManager` design.

**Alternatives considered**: PostgreSQL `SELECT FOR UPDATE SKIP LOCKED` — lighter but requires the lock to be held for the entire publish duration; a crash forfeits the lock and the event must wait for timeout. Column-based claiming makes crash recovery explicit and auditable.

---

### Decision 2: MassTransit for RabbitMQ publishing (not raw RabbitMQ.Client)

**Choice**: Use MassTransit's `ISendEndpoint` / `IPublishEndpoint` for routing events to RabbitMQ exchanges.

**Rationale**: MassTransit is already the platform standard (used by existing microservices). Reusing it avoids a second RabbitMQ connection abstraction, provides built-in retry and error handling hooks, and ensures message envelopes are compatible with existing consumers.

**Alternatives considered**: Raw `RabbitMQ.Client` — lower overhead but requires manual connection/channel lifecycle management and produces incompatible message envelopes for MassTransit consumers.

---

### Decision 3: Raw JSON payload forwarded as-is (no deserialization in the worker)

**Choice**: The worker publishes `OutboxEvent.Payload` (pre-serialized JSON string) directly as the MassTransit message body. The worker does NOT deserialize into a typed `SharedKernel` contract before publishing.

**Rationale**: Decouples the worker from contract versioning. If a `SharedKernel` contract changes, the worker needs no update — it forwards bytes. Consumers receive the same JSON they would if the producer had published directly.

**Impact on SharedKernel dependency**: The worker still references `SharedKernel` for the `EventType` name strings used in configuration validation, but does not instantiate contract types at runtime.

**Alternatives considered**: Deserialize payload into a typed object and re-serialize — adds a round-trip with no benefit; breaks if the payload has fields unknown to the current `SharedKernel` version.

---

### Decision 4: Per-EventType routing via `appsettings.json` (not code-based registry)

**Choice**: Publisher routes are declared in configuration:
```json
"Publishers": {
  "CustomerCreated": { "Exchange": "crm.events", "RoutingKey": "customer.created" },
  "InvoiceCreated":  { "Exchange": "billing.events", "RoutingKey": "invoice.created" }
}
```
If an `EventType` has no entry, the event is marked `Skipped` (preserved in history).

**Rationale**: Operations can add new routes without redeploying code. `Skipped` status ensures events from new event types are not lost while routes are being configured.

**Alternatives considered**: Code-based `IPublisherFactory` registration — requires recompile/redeploy for every new event type; not suitable for a platform-wide shared service.

---

### Decision 5: Project structure within `SmartCoreOutbox.sln`

```
Src/
└── OutboxWorker/
    ├── Program.cs
    ├── OutboxProcessor.cs          ← BackgroundService
    ├── ClaimManager.cs
    ├── Publishers/
    │   ├── IEventPublisher.cs
    │   ├── IPublisherFactory.cs
    │   ├── PublisherRoute.cs       ← config model
    │   └── RabbitMq/
    │       ├── RabbitMqPublisher.cs
    │       └── RabbitMqPublisherOptions.cs
    ├── Configuration/
    │   ├── OutboxWorkerOptions.cs
    │   └── RabbitMqOptions.cs
    └── appsettings.json
Tests/
└── OutboxWorker.IntegrationTests/
```

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Worker crashes mid-batch leaving events claimed | Stale claim recovery: events with `ClaimedAt < now() - ClaimTimeout` are released on each polling cycle |
| RabbitMQ unavailable — events pile up in `Pending` | Worker logs error, releases claim, retries on next poll interval; no data loss |
| `MaxRetries` exceeded — event stuck in `Failed` | Ops must manually reset `Status = 'Pending'` or investigate; `LastError` column provides context |
| Single instance is a SPOF for event delivery | Acceptable for current scale; Docker restart policy provides auto-recovery; claim timeout prevents permanent stuck events |
| `Skipped` events grow unboundedly | Events are preserved (not deleted); archival/cleanup policy is a future concern |
| appsettings route misconfiguration sends to wrong exchange | Config validation on startup: verify all `Publishers` entries have non-empty `Exchange` and `RoutingKey` |

## Migration Plan

1. Ensure `SmartCore.Outbox` NuGet v1.0.0 is published and `outbox_db` schema exists
2. Build and push `outbox-worker` Docker image via GitHub Actions
3. Add `outbox-worker` service to `docker-compose.yml` with `outbox_db` and RabbitMQ URIs
4. Configure `Publishers` section in `appsettings.json` for all known EventTypes
5. Deploy; verify events transition from `Pending` → `Published` in `outbox_db`

**Rollback**: Stop the `outbox-worker` container. Events remain in `Pending` state in `outbox_db` and will be delivered once the worker is restarted. No data loss.

## Open Questions

- Should the worker expose a `/health` HTTP endpoint (liveness + readiness) for Docker health checks?
- What is the retention policy for `Published` and `Skipped` events in `outbox_db`? (archival job — future)
- Should `BatchSize` and `PollingIntervalSeconds` be hot-reloadable via `IOptionsMonitor`?
