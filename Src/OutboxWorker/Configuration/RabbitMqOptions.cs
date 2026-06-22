using System.ComponentModel.DataAnnotations;

namespace OutboxWorker.Configuration;

public sealed class RabbitMqOptions
{
    [Required]
    public string Uri { get; set; } = string.Empty;
}
