## 1. Solution & Project Setup

- [x] 1.1 Add `OutboxWorker` Worker Service project (.NET 9) to `SmartCoreOutbox.sln` at `Src/OutboxWorker/OutboxWorker.csproj`
- [x] 1.2 Add `OutboxWorker.IntegrationTests` xUnit project to `SmartCoreOutbox.sln` at `Tests/OutboxWorker.IntegrationTests/`
- [x] 1.3 Add NuGet references to `OutboxWorker.csproj`: `SmartCore.Outbox` (project reference during dev), `MassTransit.RabbitMQ`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Options.DataAnnotations`
- [x] 1.4 Add NuGet references to `OutboxWorker.IntegrationTests.csproj`: `Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `MassTransit.Testing`

## 2. Configuration Models

- [x] 2.1 Create `Src/OutboxWorker/Configuration/OutboxWorkerOptions.cs` — typed options with `ConnectionString`, `PollingIntervalSeconds` (default 5), `BatchSize` (default 50), `ClaimTimeoutSeconds` (default 60), `MaxRetries` (default 5); add `[Required]` on `ConnectionString`
- [x] 2.2 Create `Src/OutboxWorker/Configuration/RabbitMqOptions.cs` — typed options with `Uri` (string, required)
- [x] 2.3 Create `Src/OutboxWorker/Publishers/PublisherRoute.cs` — record with `Exchange` (string) and `RoutingKey` (string)
- [x] 2.4 Create `Src/OutboxWorker/appsettings.json` with `Outbox`, `RabbitMq`, and `Publishers` sections; populate `Publishers` with all known SharedKernel EventType → exchange/routingKey mappings for CRM, Payments, Sales, Catalogs, Inventory, ElectronicInvoice domains

## 3. Publisher Abstractions & RabbitMQ Implementation

- [x] 3.1 Create `Src/OutboxWorker/Publishers/IEventPublisher.cs` — interface with `PublishAsync(OutboxEvent, CancellationToken)`
- [x] 3.2 Create `Src/OutboxWorker/Publishers/IPublisherFactory.cs` — interface with `GetPublisher(string eventType)` returning `IEventPublisher?`
- [x] 3.3 Create `Src/OutboxWorker/Publishers/RabbitMq/RabbitMqPublisher.cs` — implements `IEventPublisher`; uses MassTransit `ISendEndpointProvider` to send raw JSON payload to configured exchange and routing key; does NOT deserialize payload
- [x] 3.4 Create `Src/OutboxWorker/Publishers/ConfigurationPublisherFactory.cs` — implements `IPublisherFactory`; looks up `PublisherRoute` from `IOptions<Dictionary<string, PublisherRoute>>`; returns `RabbitMqPublisher` for known types, `null` for unknown
- [x] 3.5 Add startup validation in `Program.cs` that iterates all `Publishers` entries and throws `InvalidOperationException` if any `Exchange` or `RoutingKey` is empty

## 4. Claim Manager

- [x] 4.1 Create `Src/OutboxWorker/ClaimManager.cs` — uses `OutboxDbContext` with raw SQL (or EF `ExecuteSqlRaw`) for atomic `UPDATE...RETURNING` batch claim; sets `ClaimedBy`, `ClaimedAt`; respects `BatchSize` and `ClaimTimeoutSeconds`
- [x] 4.2 Implement `ClaimManager.ReleaseStaleClaimsAsync` — single `UPDATE` that sets `ClaimedBy = NULL`, `ClaimedAt = NULL` for rows where `Status = 'Pending'` AND `ClaimedAt < now() - ClaimTimeout`
- [x] 4.3 Generate unique instance ID in `ClaimManager` constructor using `$"{Environment.MachineName}:{Guid.NewGuid()}"`

## 5. Outbox Processor

- [x] 5.1 Create `Src/OutboxWorker/OutboxProcessor.cs` — implements `BackgroundService`; inject `ClaimManager`, `IPublisherFactory`, `OutboxDbContext`, `IOptions<OutboxWorkerOptions>`, `ILogger`
- [x] 5.2 Implement `ExecuteAsync` loop: call `ReleaseStaleClaimsAsync`, then `ClaimBatchAsync`, then process each event; catch per-event exceptions without aborting the batch; wait `PollingIntervalSeconds` between iterations; respect `CancellationToken`
- [x] 5.3 Implement event outcome updates in `OutboxProcessor`: `Published` (set `Status`, `PublishedAt`, clear claim), `Skipped` (set `Status`), retry (increment `RetryCount`, clear claim), `Failed` (set `Status`, `LastError`) when `RetryCount >= MaxRetries`

## 6. Program.cs Wiring

- [x] 6.1 Create `Src/OutboxWorker/Program.cs` — configure `OutboxDbContext` (reuse connection string from `OutboxWorkerOptions`), bind `OutboxWorkerOptions` and `RabbitMqOptions` from config, register MassTransit with RabbitMQ using `RabbitMqOptions.Uri`, register `ClaimManager`, `IPublisherFactory`, and `OutboxProcessor` as `BackgroundService`
- [x] 6.2 Add `IOptions` validation on startup: call `services.AddOptions<OutboxWorkerOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`

## 7. Integration Tests

- [x] 7.1 Set up `PostgreSqlContainer` and `RabbitMqContainer` (Testcontainers) fixtures shared across the test class in `Tests/OutboxWorker.IntegrationTests/`
- [x] 7.2 Add integration test: `ClaimManager.ClaimBatchAsync` returns up to `BatchSize` pending events and marks them with the instance ID
- [x] 7.3 Add integration test: `ClaimManager.ReleaseStaleClaimsAsync` releases events with expired `ClaimedAt` and leaves fresh claims untouched
- [x] 7.4 Add integration test: `OutboxProcessor` processes a `Pending` event end-to-end — event appears in RabbitMQ and row transitions to `Status = 'Published'`
- [x] 7.5 Add integration test: event with `EventType` having no publisher route transitions to `Status = 'Skipped'`
- [x] 7.6 Add integration test: event that exceeds `MaxRetries` transitions to `Status = 'Failed'` with `LastError` populated
- [x] 7.7 Add integration test: per-event error isolation — one failing event does not prevent others in the same batch from being published

## 8. Docker & Deployment

- [x] 8.1 Create `Src/OutboxWorker/Dockerfile` — multi-stage build (`dotnet restore` → `dotnet publish`) producing a minimal runtime image
- [x] 8.2 Add `outbox-worker` service entry to `docker-compose.yml` (or create one if not existing) with environment variable overrides for `Outbox__ConnectionString` and `RabbitMq__Uri`
- [x] 8.3 Create `.github/workflows/worker-publish.yml` — CI pipeline that builds, runs integration tests, and pushes Docker image to container registry on push to `main`
