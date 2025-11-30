using System.ComponentModel.DataAnnotations;

namespace PlayerQueueService.Api.Models.Configuration;

/// <summary>
/// Settings used to connect to RabbitMQ and describe the queue topology.
/// </summary>
public sealed record RabbitMQSettings
{
    /// <summary>
    /// RabbitMQ host name or IP address.
    /// </summary>
    [Required]
    public string HostName { get; init; } = "localhost";

    /// <summary>
    /// RabbitMQ TCP port.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    /// <summary>
    /// Username for authenticating with RabbitMQ. If null, the client default is used.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Password for authenticating with RabbitMQ. If null, the client default is used.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// The RabbitMQ virtual host to connect to.
    /// </summary>
    public string VirtualHost { get; init; } = "/";

    /// <summary>
    /// Name of the queue that player events are published to.
    /// </summary>
    [Required]
    public string QueueName { get; init; } = "player-queue.enqueued";

    /// <summary>
    /// Name of the exchange used for player events.
    /// </summary>
    [Required]
    public string ExchangeName { get; init; } = "player-queue";

    /// <summary>
    /// Routing key used when publishing player events.
    /// </summary>
    [Required]
    public string RoutingKey { get; init; } = "player.queue.enqueued";

    /// <summary>
    /// Maximum number of messages the consumer will prefetch.
    /// </summary>
    [Range(1, 1000)]
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>
    /// Heartbeat interval in seconds for the RabbitMQ connection.
    /// </summary>
    [Range(10, 300)]
    public int HeartbeatSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum processing retry attempts for the consumer.
    /// </summary>
    [Range(1, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay in seconds between consumer retry attempts.
    /// </summary>
    [Range(1, 120)]
    public int RetryDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Confirmation timeout in seconds when publishing messages.
    /// </summary>
    [Range(1, 60)]
    public int PublishConfirmTimeoutSeconds { get; init; } = 5;
}
