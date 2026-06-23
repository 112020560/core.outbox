namespace OutboxWorker.Publishers;

internal interface IMessageTypeRegistry
{
    Type? Resolve(string eventType);
}
