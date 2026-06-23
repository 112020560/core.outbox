namespace SmartCore.Outbox.Infrastructure;

public record ProcessedEvent
{
    public Guid EventId { get; init; }
    public string ConsumerName { get; init; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}
