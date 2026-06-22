## Context

Microservices in the SmartCore platform currently publish domain events directly to RabbitMQ inside command handlers. This creates a reliability gap: if the broker is unavailable or the process crashes after a domain `SaveChanges()` but before the publish call, the event is silently lost. There is also no durable event history.

The `SmartCore.Outbox` NuGet package solves the write side of this problem by durably persisting events to a dedicated PostgreSQL database (`outbox_db`) immediately after the domain commit. Delivery to RabbitMQ is the responsibility of the separate `outbox-worker` service (Phase 2).

## Goals / Non-Goals

**Goals:**
- Provide a single-call registration API (`AddSmartOutbox`) usable in any .NET 9 microservice
- Guarantee durable event persistence to `outbox_db` with deduplication via `DeduplicationKey`
- Expose `IIdempotencyGuard` so consumers can prevent double-processing of redelivered messages
- Embed and auto-apply EF Core migrations on host startup so consuming services need no DB setup
- Keep the library free of any dependency on `SharedKernel` (payload is opaque JSON)

**Non-Goals:**
- Event delivery, polling, or routing to RabbitMQ (handled by `outbox-worker`)
- Transactional atomicity with the domain database (separate DB by design — see Decisions)
- Deserialization or schema validation of event payloads
- Multi-tenant or per-service partitioning within `outbox_db`
- Distributed tracing / OpenTelemetry instrumentation

## Decisions

### Decision 1: Dedicated `outbox_db` (not same-DB schema)

**Choice**: All microservices write to a single shared `outbox_db` PostgreSQL instance that is managed exclusively by `SmartCore.Outbox`.

**Rationale**: Sharing the domain DB would require the NuGet to know the consumer's DbContext or connection, creating tight coupling. A dedicated DB gives the outbox full schema ownership, allows independent scaling, and is the only option when services run on heterogeneous databases.

**Trade-off**: Outbox writes are NOT in the same transaction as domain commits. Mitigation: domain commit runs first (source of truth), then `TryAppendAsync` returns `false` on failure without throwing. Events can be reinserted via aggregate replay.

**Alternatives considered**: Same-DB schema with a shared `IDbConnection` — rejected because it couples the NuGet to each service's DB provider and connection lifecycle.

---

### Decision 2: EF Core 9 + Npgsql (not Dapper or raw SQL)

**Choice**: `OutboxDbContext` uses EF Core 9 with Npgsql provider. Migrations are embedded and auto-applied at startup via `context.Database.MigrateAsync()`.

**Rationale**: Consistent with the existing platform stack. EF Core migrations give schema evolution tracking for free. Auto-apply on startup removes the need for manual migration steps in consuming services.

**Alternatives considered**: Dapper with hand-crafted SQL — simpler but no migration story, requires consumers to run scripts manually.

---

### Decision 3: `IOutboxWriter` writes raw JSON string (no SharedKernel dependency)

**Choice**: `OutboxEvent.Payload` is a `string` (pre-serialized JSON by the caller). The NuGet takes no dependency on `SharedKernel`.

**Rationale**: The outbox is a generic persistence mechanism. Coupling it to specific contract types (which evolve per domain) would make the NuGet a versioning bottleneck. Callers serialize with `System.Text.Json` before calling `AppendAsync`.

**Alternatives considered**: Generic `AppendAsync<T>(T payload)` that serializes internally — rejected because it hides serialization behavior and still requires SharedKernel transitively.

---

### Decision 4: `Guid.CreateVersion7()` for event IDs

**Choice**: `OutboxEvent.Id` defaults to `Guid.CreateVersion7()` (time-ordered UUID, .NET 9 built-in).

**Rationale**: UUIDv7 is time-ordered, which improves B-tree index performance on the `Events` table PRIMARY KEY. No external library needed (.NET 9 native).

**Alternatives considered**: `Guid.NewGuid()` (random v4) — worse index fragmentation at scale.

---

### Decision 5: Project structure within `SmartCoreOutbox.sln`

```
SmartCoreOutbox.sln
└── Src/
    └── SmartCore.Outbox/
        ├── Abstractions/        IOutboxWriter.cs, IIdempotencyGuard.cs
        ├── Models/              OutboxEvent.cs
        ├── Infrastructure/      OutboxDbContext.cs, OutboxRepository.cs, Migrations/
        └── Extensions/          ServiceCollectionExtensions.cs
Tests/
    ├── SmartCore.Outbox.UnitTests/
    └── SmartCore.Outbox.IntegrationTests/   (Testcontainers)
```

This keeps the Worker Service projects (Phase 2) at the same level once added.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Outbox write fails after domain commit | `TryAppendAsync` returns `false`; caller logs warning; event replayable from domain aggregate |
| `DeduplicationKey` collision on different logical events | Callers must use a semantically unique key (e.g., `"CustomerCreated:{customerId}"`) — documented as a caller contract |
| `outbox_db` unavailable at startup (migration step) | Wrap `MigrateAsync` in a retry policy with exponential backoff; configurable via `OutboxOptions` |
| EF Core migration conflicts if multiple service instances start simultaneously | PostgreSQL advisory locks used internally by EF Core's migration history table prevent concurrent migration races |
| Package version drift across many microservices | Semantic versioning + GitHub Actions NuGet publish; breaking changes require major version bump |

## Migration Plan

1. Publish `SmartCore.Outbox` v1.0.0 to GitHub Packages (internal feed)
2. In each target microservice:
   - Add `<PackageReference Include="SmartCore.Outbox" />` to the `.csproj`
   - Add `builder.Services.AddSmartOutbox(o => { o.ConnectionString = ...; o.ServiceName = ...; })` to `Program.cs`
   - Replace direct `IRabbitMqProducer.PublishEvent()` calls with `IOutboxWriter.AppendAsync()`
3. `outbox_db` is created and migrated automatically on first microservice startup
4. Deploy `outbox-worker` (Phase 2) pointed at the same `outbox_db`

**Rollback**: Remove `AddSmartOutbox` registration and revert to direct publish calls. Events already in `outbox_db` are preserved and can be replayed once re-deployed.

## Open Questions

- Should `OutboxOptions` expose a `RetryPolicy` for the migration step, or rely on the host's readiness probe to restart if DB is unavailable?
- Should the package expose a health check (`IHealthCheck`) for `outbox_db` connectivity?
