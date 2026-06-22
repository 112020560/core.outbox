## ADDED Requirements

### Requirement: IEventPublisher abstraction
The system SHALL define `IEventPublisher` with a single method `PublishAsync(OutboxEvent, CancellationToken)` returning `Task`. Implementations are responsible for delivering the event payload to a specific destination.

#### Scenario: Publisher called with claimed event
- **WHEN** `IEventPublisher.PublishAsync` is called with a claimed `OutboxEvent`
- **THEN** the event payload is delivered to the configured destination

### Requirement: IPublisherFactory resolves publisher by EventType
The system SHALL provide `IPublisherFactory.GetPublisher(string eventType)` returning `IEventPublisher?`. Returns `null` if no publisher is configured for the given `eventType`.

#### Scenario: Known EventType returns publisher
- **WHEN** `GetPublisher("CustomerCreated")` is called and a route is configured for `CustomerCreated`
- **THEN** an `IEventPublisher` instance is returned

#### Scenario: Unknown EventType returns null
- **WHEN** `GetPublisher("UnknownEventType")` is called and no route is configured
- **THEN** `null` is returned

### Requirement: Published outcome
After a successful `IEventPublisher.PublishAsync`, the system SHALL update the event row to `Status = 'Published'`, `PublishedAt = now()`, `ClaimedBy = NULL`, `ClaimedAt = NULL`.

#### Scenario: Status updated to Published
- **WHEN** publishing succeeds for a claimed event
- **THEN** `Events` row has `Status = 'Published'` and `PublishedAt` is set to current UTC

### Requirement: Skipped outcome
When `IPublisherFactory.GetPublisher` returns `null`, the system SHALL update the event row to `Status = 'Skipped'` without attempting delivery. The event is preserved in history.

#### Scenario: No route configured marks event Skipped
- **WHEN** an event's `EventType` has no publisher route configured
- **THEN** `Events` row is updated to `Status = 'Skipped'` and the event is not retried

### Requirement: Failed outcome after max retries
When `IEventPublisher.PublishAsync` throws, the system SHALL increment `RetryCount` and release the claim (`ClaimedBy = NULL`, `ClaimedAt = NULL`). If `RetryCount >= MaxRetries`, the system SHALL update `Status = 'Failed'` and record the error in `LastError`.

#### Scenario: Publish error increments RetryCount
- **WHEN** `PublishAsync` throws and `RetryCount < MaxRetries`
- **THEN** `RetryCount` is incremented, `ClaimedBy` and `ClaimedAt` are cleared, and `Status` remains `'Pending'`

#### Scenario: MaxRetries exceeded marks event Failed
- **WHEN** `PublishAsync` throws and `RetryCount >= MaxRetries`
- **THEN** `Status = 'Failed'`, `LastError` contains the exception message, and the event is not retried
