namespace OutboxWorker.Publishers;

public sealed record PublisherRoute(
    RouteType RouteType,
    string Exchange = "",
    string RoutingKey = "",
    string? Queue = null);
