namespace SmartCore.Outbox.Models;

public static class OutboxEventStatus
{
    public const string Pending = "Pending";
    public const string Published = "Published";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
}
