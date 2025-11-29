using System.ComponentModel.DataAnnotations;

namespace PlayerQueueService.Api.Models.Configuration;

public class RabbitMqOptions
{
    [Required]
    public string HostName { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    [Required]
    public string QueueName { get; set; } = "player-queue.enqueued";

    [Required]
    public string ExchangeName { get; set; } = "player-queue";

    [Required]
    public string RoutingKey { get; set; } = "player.queue.enqueued";

    [Range(1, 1000)]
    public ushort PrefetchCount { get; set; } = 10;

    [Range(10, 300)]
    public int HeartbeatSeconds { get; set; } = 60;

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(1, 120)]
    public int RetryDelaySeconds { get; set; } = 2;

    [Range(1, 60)]
    public int PublishConfirmTimeoutSeconds { get; set; } = 5;
}
