namespace OutboxWorker.Publishers;

public sealed record PublisherRoute(string Exchange, string RoutingKey);
