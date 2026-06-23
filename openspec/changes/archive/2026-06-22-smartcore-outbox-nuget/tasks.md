## 1. Solution & Project Setup

- [x] 1.1 Add `SmartCore.Outbox` Class Library project (.NET 9) to `SmartCoreOutbox.sln` at `Src/SmartCore.Outbox/SmartCore.Outbox.csproj`
- [x] 1.2 Add `SmartCore.Outbox.UnitTests` xUnit project to `SmartCoreOutbox.sln` at `Tests/SmartCore.Outbox.UnitTests/`
- [x] 1.3 Add `SmartCore.Outbox.IntegrationTests` xUnit project to `SmartCoreOutbox.sln` at `Tests/SmartCore.Outbox.IntegrationTests/`
- [x] 1.4 Add NuGet references to `SmartCore.Outbox.csproj`: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`
- [x] 1.5 Add NuGet references to `SmartCore.Outbox.IntegrationTests.csproj`: `Testcontainers.PostgreSql`, `Microsoft.EntityFrameworkCore.Design`

## 2. Domain Model

- [x] 2.1 Create `Src/SmartCore.Outbox/Models/OutboxEvent.cs` — immutable record with `Id` (UUIDv7 default), `ServiceName`, `DeduplicationKey`, `AggregateId`, `AggregateType`, `EventType`, `Payload`, `OccurredAt` (UTC default)
- [x] 2.2 Create `Src/SmartCore.Outbox/Models/OutboxEventStatus.cs` — enum or static class with values: `Pending`, `Published`, `Skipped`, `Failed`

## 3. Abstractions

- [x] 3.1 Create `Src/SmartCore.Outbox/Abstractions/IOutboxWriter.cs` — interface with `AppendAsync(OutboxEvent, CancellationToken)` and `TryAppendAsync(OutboxEvent, CancellationToken)` returning `Task<bool>`
- [x] 3.2 Create `Src/SmartCore.Outbox/Abstractions/IIdempotencyGuard.cs` — interface with `AlreadyProcessedAsync(Guid, string, CancellationToken)` and `MarkAsProcessedAsync(Guid, string, CancellationToken)`

## 4. Infrastructure — DbContext & Schema

- [x] 4.1 Create `Src/SmartCore.Outbox/Infrastructure/OutboxDbContext.cs` — `DbContext` with `DbSet<OutboxEvent> Events` and `DbSet<ProcessedEvent> ProcessedEvents`; configure table names, column types, UNIQUE constraint on `DeduplicationKey`, and composite PK on `ProcessedEvents`
- [x] 4.2 Create `Src/SmartCore.Outbox/Infrastructure/ProcessedEvent.cs` — entity record with `EventId`, `ConsumerName`, `ProcessedAt`
- [x] 4.3 Run `dotnet ef migrations add InitialSchema` in `SmartCore.Outbox` project to generate `Migrations/` folder with `Events` and `ProcessedEvents` DDL
- [x] 4.4 Verify generated migration SQL includes: `Events` table with all columns, `UNIQUE` on `DeduplicationKey`, partial index `idx_events_status_claimedat WHERE Status = 'Pending'`, `ProcessedEvents` with composite PK

## 5. Infrastructure — Repository

- [x] 5.1 Create `Src/SmartCore.Outbox/Infrastructure/OutboxRepository.cs` — implements `IOutboxWriter`; `AppendAsync` uses `DbContext.Events.AddAsync` + `SaveChangesAsync`; handles `DeduplicationKey` conflict with ON CONFLICT DO NOTHING via raw SQL or EF upsert
- [x] 5.2 Implement `TryAppendAsync` in `OutboxRepository.cs` — wraps `AppendAsync` in try/catch, returns `false` on any exception
- [x] 5.3 Create `Src/SmartCore.Outbox/Infrastructure/IdempotencyRepository.cs` — implements `IIdempotencyGuard`; `AlreadyProcessedAsync` queries `ProcessedEvents` by PK; `MarkAsProcessedAsync` inserts with ON CONFLICT DO NOTHING

## 6. Service Registration

- [x] 6.1 Create `Src/SmartCore.Outbox/Extensions/OutboxOptions.cs` — POCO with `ConnectionString` (required) and `ServiceName` (required)
- [x] 6.2 Create `Src/SmartCore.Outbox/Extensions/ServiceCollectionExtensions.cs` — `AddSmartOutbox(Action<OutboxOptions>)` registers `OutboxDbContext` (scoped), `IOutboxWriter` → `OutboxRepository` (scoped), `IIdempotencyGuard` → `IdempotencyRepository` (scoped), and an `IHostedService` for auto-migration
- [x] 6.3 Create `Src/SmartCore.Outbox/Extensions/OutboxMigrationHostedService.cs` — `IHostedService` that calls `OutboxDbContext.Database.MigrateAsync()` on `StartAsync`; validates that `ConnectionString` and `ServiceName` are not empty, throws `InvalidOperationException` otherwise

## 7. Unit Tests

- [x] 7.1 Add unit tests in `Tests/SmartCore.Outbox.UnitTests/` for `OutboxEvent` default values (UUIDv7 Id, UTC OccurredAt)
- [x] 7.2 Add unit tests for `TryAppendAsync` — mock `IOutboxWriter` dependency, verify `false` returned on exception, `true` on success
- [x] 7.3 Add unit tests for `ServiceCollectionExtensions` — verify `IOutboxWriter` and `IIdempotencyGuard` are registered and resolvable from a test `ServiceCollection`
- [x] 7.4 Add unit tests for `OutboxMigrationHostedService` — verify `InvalidOperationException` thrown when `ConnectionString` is empty

## 8. Integration Tests

- [x] 8.1 Set up `PostgreSqlContainer` (Testcontainers) fixture in `Tests/SmartCore.Outbox.IntegrationTests/` shared across test class
- [x] 8.2 Add integration test: `AppendAsync` inserts row with `Status = 'Pending'` and correct field values
- [x] 8.3 Add integration test: second `AppendAsync` with same `DeduplicationKey` does not insert duplicate (returns without error)
- [x] 8.4 Add integration test: `TryAppendAsync` returns `true` on success and `false` when connection is broken
- [x] 8.5 Add integration test: `AlreadyProcessedAsync` returns `false` before mark, `true` after `MarkAsProcessedAsync`
- [x] 8.6 Add integration test: concurrent `MarkAsProcessedAsync` calls with same key produce exactly one row

## 9. NuGet Packaging & CI

- [x] 9.1 Configure `SmartCore.Outbox.csproj` with NuGet metadata: `PackageId`, `Version`, `Authors`, `Description`, `RepositoryUrl`
- [x] 9.2 Create `.github/workflows/nuget-publish.yml` — CI pipeline that builds, runs tests, packs, and publishes to GitHub Packages on push to `main`
