using MassTransit;
using Microsoft.Extensions.Options;
using OutboxWorker.Publishers.RabbitMq;

namespace OutboxWorker.Publishers;

internal sealed class ConfigurationPublisherFactory(
    ISendEndpointProvider sendEndpointProvider,
    IOptions<Dictionary<string, PublisherRoute>> routesOptions) : IPublisherFactory
{
    private readonly Dictionary<string, PublisherRoute> _routes = routesOptions.Value;

    public IEventPublisher? GetPublisher(string eventType)
    {
        if (_routes.TryGetValue(eventType, out var route))
            return new RabbitMqPublisher(sendEndpointProvider, route);

        return null;
    }
}
