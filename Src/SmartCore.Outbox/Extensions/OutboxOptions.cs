namespace SmartCore.Outbox.Extensions;

public sealed class OutboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
}
