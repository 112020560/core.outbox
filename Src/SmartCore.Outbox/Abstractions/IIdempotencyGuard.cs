namespace SmartCore.Outbox.Abstractions;

public interface IIdempotencyGuard
{
    Task<bool> AlreadyProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);
    Task MarkAsProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);
}
