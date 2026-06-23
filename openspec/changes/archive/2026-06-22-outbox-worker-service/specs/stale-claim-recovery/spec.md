## ADDED Requirements

### Requirement: Release stale claims on each polling cycle
At the start of each polling cycle (before or after claiming a new batch), the system SHALL release claims where `Status = 'Pending'` AND `ClaimedAt < now() - ClaimTimeout`. Released rows SHALL have `ClaimedBy = NULL` and `ClaimedAt = NULL`, making them eligible for re-claiming.

#### Scenario: Stale claim released after timeout
- **WHEN** an event has `ClaimedAt = now() - 90s` and `ClaimTimeout = 60s`
- **THEN** the recovery step sets `ClaimedBy = NULL` and `ClaimedAt = NULL` on that row

#### Scenario: Fresh claim is not released
- **WHEN** an event has `ClaimedAt = now() - 10s` and `ClaimTimeout = 60s`
- **THEN** the recovery step does NOT modify that row

### Requirement: Stale claim recovery is atomic
The stale claim release SHALL execute as a single `UPDATE` statement so it does not interfere with concurrent claim operations from the same or other worker instances.

#### Scenario: Recovery and claim do not conflict
- **WHEN** stale claim recovery and a new batch claim run near-simultaneously
- **THEN** no event is both released and claimed in the same operation; each event ends up in one deterministic state
