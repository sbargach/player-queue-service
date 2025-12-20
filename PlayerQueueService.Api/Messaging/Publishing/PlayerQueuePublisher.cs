using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Telemetry;
using Polly;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace PlayerQueueService.Api.Messaging.Publishing;

public sealed class PlayerQueuePublisher : IPlayerQueuePublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMQSettings _settings;
    private readonly ILogger<PlayerQueuePublisher> _logger;
    private readonly IMetricsProvider _metrics;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public PlayerQueuePublisher(
        IRabbitMqConnection connection,
        IOptions<RabbitMQSettings> settings,
        ILogger<PlayerQueuePublisher> logger,
        IMetricsProvider metrics)
    {
        _connection = connection;
        _settings = settings.Value;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task PublishAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = Tracing.ActivitySource.StartActivity(
            "rabbitmq.publish",
            ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _settings.ExchangeName);
        activity?.SetTag("messaging.rabbitmq.routing_key", _settings.RoutingKey);
        activity?.SetTag("player.id", playerEvent.PlayerId);
        activity?.SetTag("player.region", playerEvent.Region);
        activity?.SetTag("player.mode", playerEvent.GameMode);

        var retryPolicy = Policy
            .Handle<Exception>(IsTransientPublishException)
            .RetryForeverAsync(
                async (exception, attempt, _) =>
                {
                    _logger.LogError(
                        exception,
                        "Publishing {PlayerId} to RabbitMQ queue {QueueName} resulted in an error. Retrying attempt {Attempt}",
                        playerEvent.PlayerId,
                        _settings.QueueName,
                        attempt);
                    _metrics.IncrementPublishRetry(playerEvent, _settings.QueueName);
                    await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(false);
                });

        await retryPolicy
            .ExecuteAsync((_, ct) => PublishOnceAsync(playerEvent, ct), new Context("rabbitmq-publisher"), cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Published player {PlayerId} for mode {Mode} in region {Region}",
            playerEvent.PlayerId,
            playerEvent.GameMode,
            playerEvent.Region);
    }

    private async Task PublishOnceAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _metrics.IncrementPublishAttempt(playerEvent);

        using var activity = Tracing.ActivitySource.StartActivity(
            "rabbitmq.publish.attempt",
            ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _settings.ExchangeName);
        activity?.SetTag("messaging.rabbitmq.routing_key", _settings.RoutingKey);
        activity?.SetTag("player.id", playerEvent.PlayerId);
        activity?.SetTag("player.region", playerEvent.Region);
        activity?.SetTag("player.mode", playerEvent.GameMode);

        var stopwatch = Stopwatch.StartNew();
        try
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
            InjectTraceContext(activity, properties);

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
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            _metrics.IncrementPublishFailure(playerEvent, "canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _metrics.IncrementPublishFailure(playerEvent, ex.GetType().Name);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }

        _metrics.IncrementPublishSuccess(playerEvent);
        _metrics.RecordPublishDuration(playerEvent, stopwatch.Elapsed.TotalMilliseconds);
    }

    private static void InjectTraceContext(Activity? activity, IBasicProperties properties)
    {
        if (activity is null)
        {
            return;
        }

        var carrier = new Dictionary<string, string?>();
        Tracing.Propagator.Inject(
            new PropagationContext(activity.Context, default),
            carrier,
            static (dict, key, value) => dict[key] = value);

        if (carrier.Count == 0)
        {
            return;
        }

        properties.Headers ??= new Dictionary<string, object?>();
        foreach (var kvp in carrier)
        {
            if (kvp.Value is null)
            {
                continue;
            }

            properties.Headers[kvp.Key] = Encoding.UTF8.GetBytes(kvp.Value);
        }
    }

    private static bool IsTransientPublishException(Exception exception) =>
        RabbitMqTransientExceptionClassifier.IsTransient(exception);
}
