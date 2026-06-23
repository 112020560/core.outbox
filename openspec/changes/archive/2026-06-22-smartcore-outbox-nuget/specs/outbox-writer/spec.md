## ADDED Requirements

### Requirement: Append event to outbox
The system SHALL provide `IOutboxWriter.AppendAsync(OutboxEvent, CancellationToken)` that persists an `OutboxEvent` to the `Events` table in `outbox_db`. The method SHALL throw if the insert fails (e.g., DB unavailable).

#### Scenario: Successful append
- **WHEN** a caller invokes `AppendAsync` with a valid `OutboxEvent`
- **THEN** a row is inserted in the `Events` table with `Status = 'Pending'`, `RetryCount = 0`, and all provided fields persisted

#### Scenario: Append fails due to DB error
- **WHEN** `AppendAsync` is called and `outbox_db` is unreachable
- **THEN** an exception is thrown and no row is inserted

### Requirement: Try-append with non-throwing semantics
The system SHALL provide `IOutboxWriter.TryAppendAsync(OutboxEvent, CancellationToken)` that returns `true` on success and `false` on any failure, without throwing.

#### Scenario: Successful try-append
- **WHEN** `TryAppendAsync` is called with a valid `OutboxEvent` and the DB is reachable
- **THEN** the event is persisted and `true` is returned

#### Scenario: Failed try-append
- **WHEN** `TryAppendAsync` is called and an exception occurs during insert
- **THEN** `false` is returned and no exception propagates to the caller

### Requirement: Deduplication via DeduplicationKey
The system SHALL enforce uniqueness on `DeduplicationKey` at the database level. A second insert with the same `DeduplicationKey` SHALL be silently ignored (upsert / ON CONFLICT DO NOTHING semantics).

#### Scenario: Duplicate key on AppendAsync
- **WHEN** `AppendAsync` is called twice with the same `DeduplicationKey`
- **THEN** only the first row is stored; the second call completes without error and without inserting a duplicate

#### Scenario: Duplicate key on TryAppendAsync
- **WHEN** `TryAppendAsync` is called with a `DeduplicationKey` that already exists
- **THEN** `true` is returned and no duplicate row is created

### Requirement: OutboxEvent model fields
The `OutboxEvent` record SHALL expose the following init-only properties:
- `Id` (Guid, defaults to `Guid.CreateVersion7()`)
- `ServiceName` (string, required)
- `DeduplicationKey` (string, required)
- `AggregateId` (Guid, required)
- `AggregateType` (string, required)
- `EventType` (string, required)
- `Payload` (string, required — caller-serialized JSON)
- `OccurredAt` (DateTimeOffset, defaults to `DateTimeOffset.UtcNow`)

#### Scenario: Default ID and timestamp
- **WHEN** an `OutboxEvent` is created without specifying `Id` or `OccurredAt`
- **THEN** `Id` is a time-ordered UUIDv7 and `OccurredAt` is the current UTC time
