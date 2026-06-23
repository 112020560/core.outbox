## ADDED Requirements

### Requirement: Atomic batch claim
The system SHALL claim a batch of `Pending` events atomically using a single `UPDATE...RETURNING` statement that sets `ClaimedBy` to the worker's instance ID and `ClaimedAt` to the current UTC timestamp. The batch size SHALL be bounded by `OutboxWorkerOptions.BatchSize`.

#### Scenario: Claim returns pending events
- **WHEN** the worker calls `ClaimManager.ClaimBatchAsync(batchSize, ct)`
- **THEN** up to `batchSize` rows with `Status = 'Pending'` and `ClaimedAt IS NULL` (or stale) are atomically updated with `ClaimedBy = {instanceId}` and `ClaimedAt = now()` and returned

#### Scenario: No pending events returns empty batch
- **WHEN** `ClaimBatchAsync` is called and no rows match the claim criteria
- **THEN** an empty collection is returned and no rows are modified

### Requirement: Skip already-claimed events
The claim query SHALL skip events already claimed by another worker instance (i.e., with `ClaimedAt > now() - ClaimTimeout`), so concurrent worker instances do not claim the same event.

#### Scenario: Already-claimed event is not re-claimed
- **WHEN** event A has `ClaimedAt = now() - 10s` and `ClaimTimeout = 60s`
- **THEN** event A is NOT included in a new batch claim

### Requirement: Instance ID uniqueness
Each worker instance SHALL generate a unique `ClaimedBy` identifier on startup (e.g., `$"{hostname}:{Guid.NewGuid()}"`) that persists for the lifetime of the process.

#### Scenario: Instance ID set on claimed rows
- **WHEN** a batch is claimed
- **THEN** all claimed rows have `ClaimedBy` equal to the current worker's instance ID
