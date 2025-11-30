using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace PlayerQueueService.Api.Messaging.Publishing;

public sealed class PlayerQueuePublisher : IPlayerQueuePublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMQSettings _settings;
    private readonly ILogger<PlayerQueuePublisher> _logger;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public PlayerQueuePublisher(
        IRabbitMqConnection connection,
        IOptions<RabbitMQSettings> settings,
        ILogger<PlayerQueuePublisher> logger)
    {
        _connection = connection;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task PublishAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var baseDelay = TimeSpan.FromSeconds(_settings.RetryDelaySeconds);
        var retryPolicy = Policy
            .Handle<Exception>(IsTransientPublishException)
            .WaitAndRetryForeverAsync(
                attempt => TimeSpan.FromMilliseconds(
                    Math.Min(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1), 30000)),
                (exception, delay) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Publish failed for player {PlayerId}, retrying in {Delay}",
                        playerEvent.PlayerId,
                        delay);
                    return Task.CompletedTask;
                });

        await retryPolicy.ExecuteAsync(ct => PublishOnceAsync(playerEvent, ct), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Published player {PlayerId} for mode {Mode} in region {Region}",
            playerEvent.PlayerId,
            playerEvent.GameMode,
            playerEvent.Region);
    }

    private async Task PublishOnceAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken)
    {
        using var channel = _connection.CreateChannel();
        RabbitMqTopology.EnsureQueue(channel, _settings);

        var publishReturned = new TaskCompletionSource<BasicReturnEventArgs?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReturn(object? _, BasicReturnEventArgs args) => publishReturned.TrySetResult(args);

        channel.BasicReturn += OnReturn;

        var body = JsonSerializer.SerializeToUtf8Bytes(playerEvent, SerializerOptions);

        var properties = channel.CreateBasicProperties();
        properties.MessageId = playerEvent.MessageId;
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2;
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        try
        {
            channel.ConfirmSelect();

            _logger.LogInformation(
                "Publishing player {PlayerId} for mode {Mode} in region {Region} to {Queue}",
                playerEvent.PlayerId,
                playerEvent.GameMode,
                playerEvent.Region,
                _settings.QueueName);

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);

            channel.BasicPublish(
                exchange: _settings.ExchangeName,
                routingKey: _settings.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);

            var confirmed = await Task
                .Run(() => channel.WaitForConfirms(TimeSpan.FromSeconds(_settings.PublishConfirmTimeoutSeconds)), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (publishReturned.Task.IsCompleted)
            {
                var returned = await publishReturned.Task.ConfigureAwait(false);
                throw new BrokerReturnedMessageException(
                    $"Broker returned publish for routing key '{returned?.RoutingKey}' ({returned?.ReplyCode} - {returned?.ReplyText}).");
            }

            if (!confirmed)
            {
                throw new TimeoutException(
                    $"Broker did not confirm publish within {_settings.PublishConfirmTimeoutSeconds} seconds.");
            }
        }
        finally
        {
            channel.BasicReturn -= OnReturn;
        }
    }

    private static bool IsTransientPublishException(Exception exception) =>
        exception is AlreadyClosedException
            or BrokerUnreachableException
            or OperationInterruptedException
            or RabbitMQClientException
            or IOException
            or TimeoutException;
}
