using System.Text.Json;
using MassTransit;
using SmartCore.Outbox.Models;

namespace OutboxWorker.Publishers.RabbitMq;

internal sealed class RabbitMqPublisher(
    ISendEndpointProvider sendEndpointProvider,
    IMessageTypeRegistry typeRegistry,
    PublisherRoute route) : IEventPublisher
{
    public async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
    {
        var uri = route.RouteType == RouteType.Command
            ? new Uri($"queue:{route.Queue}")
            : new Uri($"exchange:{route.Exchange}?type=fanout&routingKey={route.RoutingKey}");

        var endpoint = await sendEndpointProvider.GetSendEndpoint(uri);

        var resolvedType = typeRegistry.Resolve(outboxEvent.EventType);

        if (resolvedType is not null)
        {
            var message = JsonSerializer.Deserialize(outboxEvent.Payload, resolvedType)!;
            await endpoint.Send(message, resolvedType, ct);
        }
        else
        {
            // Fallback para tipos desconocidos — el OutboxProcessor marcará el evento como Skipped
            // si no hay publisher configurado; si llega aquí es porque el route existe pero el tipo no
            await endpoint.Send(new RawJsonMessage(outboxEvent.Payload, outboxEvent.EventType), ct);
        }
    }
}

// Fallback: tipos no resueltos por el registry
internal sealed record RawJsonMessage(string Payload, string EventType);
