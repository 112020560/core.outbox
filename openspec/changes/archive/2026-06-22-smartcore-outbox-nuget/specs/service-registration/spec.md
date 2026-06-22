## ADDED Requirements

### Requirement: AddSmartOutbox extension method
The system SHALL provide `IServiceCollection.AddSmartOutbox(Action<OutboxOptions>)` that registers all required services: `OutboxDbContext`, `IOutboxWriter`, and `IIdempotencyGuard`.

#### Scenario: Registration with valid options
- **WHEN** a microservice calls `AddSmartOutbox(o => { o.ConnectionString = "..."; o.ServiceName = "crm"; })`
- **THEN** `IOutboxWriter` and `IIdempotencyGuard` are resolvable from the DI container

#### Scenario: Missing ConnectionString throws at startup
- **WHEN** `AddSmartOutbox` is called with an empty or null `ConnectionString`
- **THEN** an `InvalidOperationException` is thrown during service registration or host build

### Requirement: OutboxOptions configuration
`OutboxOptions` SHALL expose:
- `ConnectionString` (string, required) — PostgreSQL connection string for `outbox_db`
- `ServiceName` (string, required) — identifies the microservice in the `ServiceName` column

#### Scenario: Options bound from configuration
- **WHEN** options are set via `IConfiguration` (e.g., `"Outbox:ConnectionString"`)
- **THEN** the values are correctly applied to `OutboxDbContext` and stored as `ServiceName` on every written event

### Requirement: OutboxDbContext is isolated from domain DbContexts
`OutboxDbContext` SHALL be registered as a separate context that does not interfere with any existing `DbContext` in the consuming service.

#### Scenario: Two DbContexts coexist
- **WHEN** a microservice has both its own domain `DbContext` and `OutboxDbContext` registered
- **THEN** both resolve independently without conflict and target their respective databases

### Requirement: Auto-migrate on startup
`AddSmartOutbox` SHALL register an `IHostedService` (or use `IHostApplicationLifetime`) that runs `OutboxDbContext.Database.MigrateAsync()` before the application starts accepting traffic.

#### Scenario: Migration runs before first request
- **WHEN** the host starts after calling `AddSmartOutbox`
- **THEN** database migrations complete before any `IOutboxWriter` method is called by the application
