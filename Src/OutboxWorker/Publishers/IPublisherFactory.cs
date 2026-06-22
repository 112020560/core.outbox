namespace OutboxWorker.Publishers;

public interface IPublisherFactory
{
    IEventPublisher? GetPublisher(string eventType);
}
