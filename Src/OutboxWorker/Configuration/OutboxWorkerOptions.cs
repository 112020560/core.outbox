using System.ComponentModel.DataAnnotations;

namespace OutboxWorker.Configuration;

public sealed class OutboxWorkerOptions
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int ClaimTimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 5;
}
