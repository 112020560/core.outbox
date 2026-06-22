using SmartCore.Outbox.Models;

namespace SmartCore.Outbox.Abstractions;

public interface IOutboxWriter
{
    Task AppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default);
    Task<bool> TryAppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default);
}
