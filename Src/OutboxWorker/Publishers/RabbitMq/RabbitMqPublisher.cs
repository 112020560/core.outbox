using MassTransit;
using SmartCore.Outbox.Models;

namespace OutboxWorker.Publishers.RabbitMq;

internal sealed class RabbitMqPublisher(ISendEndpointProvider sendEndpointProvider, PublisherRoute route) : IEventPublisher
{
    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
    {
        var endpoint = await sendEndpointProvider.GetSendEndpoint(
            new Uri($"exchange:{route.Exchange}?type=fanout&routingKey={route.RoutingKey}"));

        // Forward raw JSON payload as-is — no deserialization
        await endpoint.Send(new RawJsonMessage(outboxEvent.Payload, outboxEvent.EventType), ct);
    }
}

// Wrapper so MassTransit serializes the payload as a raw JSON body
internal sealed record RawJsonMessage(string Payload, string EventType);
