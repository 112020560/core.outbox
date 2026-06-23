## ADDED Requirements

### Requirement: OutboxWorkerOptions typed configuration
The system SHALL provide `OutboxWorkerOptions` bound from the `"Outbox"` configuration section with the following properties:
- `ConnectionString` (string, required) — PostgreSQL connection string for `outbox_db`
- `PollingIntervalSeconds` (int, default 5) — seconds between polling cycles
- `BatchSize` (int, default 50) — max events claimed per cycle
- `ClaimTimeoutSeconds` (int, default 60) — seconds before a claim is considered stale
- `MaxRetries` (int, default 5) — max publish attempts before marking event `Failed`

#### Scenario: Options bound from appsettings
- **WHEN** `appsettings.json` has `"Outbox": { "ConnectionString": "...", "BatchSize": 100 }`
- **THEN** `OutboxWorkerOptions.BatchSize` equals `100`

#### Scenario: Missing ConnectionString fails startup
- **WHEN** `ConnectionString` is empty or missing
- **THEN** the worker fails to start with a descriptive validation error

### Requirement: RabbitMqOptions typed configuration
The system SHALL provide `RabbitMqOptions` bound from the `"RabbitMq"` configuration section with:
- `Uri` (string, required) — AMQP connection URI (e.g., `amqp://user:pass@host:5672`)

#### Scenario: RabbitMq URI bound from config
- **WHEN** `appsettings.json` has `"RabbitMq": { "Uri": "amqp://..." }`
- **THEN** MassTransit uses that URI for the RabbitMQ connection

### Requirement: Publishers section maps EventType to route
The system SHALL bind a `Dictionary<string, PublisherRoute>` from the `"Publishers"` configuration section where each key is an `EventType` string and each value has `Exchange` and `RoutingKey`.

#### Scenario: All known EventTypes have routes
- **WHEN** the default `appsettings.json` is loaded
- **THEN** all SharedKernel contract type names (CRM, Payments, Sales, Catalogs, Inventory, ElectronicInvoice) have a corresponding entry in the `Publishers` dictionary

### Requirement: Startup configuration validation
On startup, the worker SHALL validate that `ConnectionString`, `RabbitMq.Uri`, and all `Publishers` entries have non-empty required fields. Invalid configuration SHALL cause the host to fail fast with a clear error message.

#### Scenario: Invalid publisher route detected at startup
- **WHEN** a publisher route entry has an empty `Exchange`
- **THEN** the worker logs a descriptive error and exits with a non-zero code before beginning the polling loop
