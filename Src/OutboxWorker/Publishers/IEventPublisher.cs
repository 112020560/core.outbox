using SmartCore.Outbox.Models;

namespace OutboxWorker.Publishers;

public interface IEventPublisher
{
    Task PublishAsync(OutboxEvent outboxEvent, CancellationToken ct = default);
}
