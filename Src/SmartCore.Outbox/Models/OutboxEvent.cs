namespace SmartCore.Outbox.Models;

public record OutboxEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string ServiceName { get; init; } = string.Empty;
    public string DeduplicationKey { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public string AggregateType { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
