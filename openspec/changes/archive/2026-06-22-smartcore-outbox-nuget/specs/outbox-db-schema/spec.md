## ADDED Requirements

### Requirement: Events table structure
The system SHALL manage an `Events` table in `outbox_db` with the following columns:
- `Id` UUID PRIMARY KEY (default `uuid_generate_v4()`)
- `ServiceName` VARCHAR(128) NOT NULL
- `DeduplicationKey` VARCHAR(256) UNIQUE
- `AggregateId` UUID NOT NULL
- `AggregateType` VARCHAR(128)
- `EventType` VARCHAR(256) NOT NULL
- `Payload` JSONB NOT NULL
- `OccurredAt` TIMESTAMPTZ NOT NULL
- `Status` VARCHAR(32) NOT NULL DEFAULT `'Pending'`
- `ClaimedBy` VARCHAR(128)
- `ClaimedAt` TIMESTAMPTZ
- `PublishedAt` TIMESTAMPTZ
- `RetryCount` INT NOT NULL DEFAULT 0
- `LastError` TEXT

#### Scenario: New event row defaults
- **WHEN** a row is inserted into `Events` without specifying `Status` or `RetryCount`
- **THEN** `Status` defaults to `'Pending'` and `RetryCount` defaults to `0`

#### Scenario: DeduplicationKey uniqueness
- **WHEN** a second row is inserted with a `DeduplicationKey` already present
- **THEN** the database rejects the insert with a unique constraint violation

### Requirement: Events table polling index
The system SHALL create an index on `(Status, ClaimedAt)` filtered to `WHERE Status = 'Pending'` to support efficient worker polling queries.

#### Scenario: Index exists after migration
- **WHEN** migrations are applied to a new `outbox_db`
- **THEN** the index `idx_events_status_claimedat` exists and is partial (filtered to `Status = 'Pending'`)

### Requirement: ProcessedEvents table structure
The system SHALL manage a `ProcessedEvents` table with:
- `EventId` UUID NOT NULL
- `ConsumerName` VARCHAR(256) NOT NULL
- `ProcessedAt` TIMESTAMPTZ NOT NULL
- PRIMARY KEY (`EventId`, `ConsumerName`)

#### Scenario: Composite primary key enforced
- **WHEN** the same `(EventId, ConsumerName)` pair is inserted twice
- **THEN** the second insert is rejected by the primary key constraint

### Requirement: Status values
The `Status` column SHALL only hold the values: `Pending`, `Published`, `Skipped`, `Failed`.

#### Scenario: Valid status transition
- **WHEN** the outbox-worker updates a row from `Pending` to `Published`
- **THEN** the `Status` column accepts the value without error

### Requirement: Auto-applied embedded migrations
The system SHALL embed EF Core migrations inside the `SmartCore.Outbox` assembly and apply them automatically during host startup via `OutboxDbContext.Database.MigrateAsync()`.

#### Scenario: First startup creates schema
- **WHEN** a microservice with `AddSmartOutbox` starts and `outbox_db` has no schema
- **THEN** all migrations run and both tables exist before the host begins accepting requests

#### Scenario: Subsequent startups are no-ops
- **WHEN** a microservice starts and all migrations have already been applied
- **THEN** no DDL is executed and startup completes without error
