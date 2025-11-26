using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Publishing;

public sealed class PlayerQueuePublisher : IPlayerQueuePublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<PlayerQueuePublisher> _logger;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public PlayerQueuePublisher(
        IRabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<PlayerQueuePublisher> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = _connection.CreateChannel();
        RabbitMqTopology.EnsureQueue(channel, _options);

        var body = JsonSerializer.SerializeToUtf8Bytes(playerEvent, SerializerOptions);

        var properties = channel.CreateBasicProperties();
        properties.MessageId = playerEvent.MessageId;
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2; // persistent
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        channel.BasicPublish(
            exchange: _options.ExchangeName,
            routingKey: _options.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: body);

        _logger.LogInformation(
            "Published player {PlayerId} for mode {Mode} in region {Region}",
            playerEvent.PlayerId,
            playerEvent.GameMode,
            playerEvent.Region);

        return Task.CompletedTask;
    }
}
