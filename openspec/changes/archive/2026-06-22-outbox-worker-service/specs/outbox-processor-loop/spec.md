## ADDED Requirements

### Requirement: BackgroundService polling loop
`OutboxProcessor` SHALL implement `BackgroundService` and execute the following sequence on each iteration:
1. Release stale claims (`ClaimManager.ReleaseStaleClaimsAsync`)
2. Claim a batch of pending events (`ClaimManager.ClaimBatchAsync`)
3. For each claimed event: resolve publisher → publish or skip → update status
4. Wait `PollingIntervalSeconds` before next iteration

#### Scenario: Full cycle completes without events
- **WHEN** no `Pending` events exist in `outbox_db`
- **THEN** the processor completes one cycle with no status updates and waits for the next interval

#### Scenario: Full cycle publishes a batch
- **WHEN** 3 `Pending` events exist and `BatchSize = 50`
- **THEN** all 3 are claimed, published, and updated to `Status = 'Published'` in one cycle

### Requirement: Graceful shutdown on CancellationToken
`OutboxProcessor` SHALL stop the polling loop cleanly when the host's `CancellationToken` is signalled. In-flight event processing SHALL complete before shutdown.

#### Scenario: Shutdown signal stops loop
- **WHEN** the host signals cancellation
- **THEN** the current batch (if any) finishes processing and the loop exits without error

### Requirement: Polling interval is configurable
The interval between polling cycles SHALL be driven by `OutboxWorkerOptions.PollingIntervalSeconds`. Default: 5 seconds.

#### Scenario: Custom interval applied
- **WHEN** `PollingIntervalSeconds = 10` is configured
- **THEN** the processor waits 10 seconds between cycles

### Requirement: Per-event error isolation
A failure while publishing one event SHALL NOT prevent processing of other events in the same batch. The failing event's claim SHALL be released and its `RetryCount` incremented; remaining events continue normally.

#### Scenario: One failure does not abort batch
- **WHEN** a batch of 5 events is claimed and event 3 throws during publish
- **THEN** events 1, 2, 4, 5 are published normally; event 3 has its claim released and `RetryCount` incremented
