## ADDED Requirements

### Requirement: Check if event already processed
The system SHALL provide `IIdempotencyGuard.AlreadyProcessedAsync(Guid eventId, string consumerName, CancellationToken)` that returns `true` if a row exists in `ProcessedEvents` for the given `(EventId, ConsumerName)` pair.

#### Scenario: Event not yet processed
- **WHEN** `AlreadyProcessedAsync` is called with an `eventId` and `consumerName` that have no entry in `ProcessedEvents`
- **THEN** `false` is returned

#### Scenario: Event already processed
- **WHEN** `AlreadyProcessedAsync` is called with an `eventId` and `consumerName` that have a matching row in `ProcessedEvents`
- **THEN** `true` is returned

### Requirement: Mark event as processed
The system SHALL provide `IIdempotencyGuard.MarkAsProcessedAsync(Guid eventId, string consumerName, CancellationToken)` that inserts a row into `ProcessedEvents` with the current UTC timestamp.

#### Scenario: Successful mark
- **WHEN** `MarkAsProcessedAsync` is called with a new `(eventId, consumerName)` pair
- **THEN** a row is inserted in `ProcessedEvents` with `ProcessedAt = UTC now`

#### Scenario: Duplicate mark is idempotent
- **WHEN** `MarkAsProcessedAsync` is called twice with the same `(eventId, consumerName)` pair
- **THEN** the second call completes without error (ON CONFLICT DO NOTHING) and no duplicate row is created

### Requirement: ProcessedEvents primary key enforcement
The `ProcessedEvents` table SHALL enforce a composite primary key on `(EventId, ConsumerName)` at the database level so that idempotency is guaranteed even under concurrent consumer instances.

#### Scenario: Concurrent mark attempts
- **WHEN** two consumer instances call `MarkAsProcessedAsync` concurrently with the same `(eventId, consumerName)`
- **THEN** exactly one row is inserted and neither call throws a duplicate key exception to the caller
