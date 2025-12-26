using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OpenTelemetry.Context.Propagation;

namespace PlayerQueueService.Api.Messaging.Consumers;

public sealed class PlayerQueueConsumer : BackgroundService
{
    private readonly IRabbitMqConnection _connection;
    private readonly IPlayerQueueProcessor _processor;
    private readonly RabbitMQSettings _settings;
    private readonly ILogger<PlayerQueueConsumer> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IMetricsProvider _metrics;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IPlayerEnqueuedEventValidator _validator;
    private CancellationToken _stoppingToken;
    private IModel? _channel;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private AsyncEventingBasicConsumer? _consumer;

    public PlayerQueueConsumer(
        IRabbitMqConnection connection,
        IPlayerQueueProcessor processor,
        IOptions<RabbitMQSettings> options,
        IHostApplicationLifetime applicationLifetime,
        ILogger<PlayerQueueConsumer> logger,
        IMetricsProvider metrics,
        IIdempotencyStore idempotencyStore,
        IPlayerEnqueuedEventValidator validator)
    {
        _connection = connection;
        _processor = processor;
        _logger = logger;
        _settings = options.Value;
        _applicationLifetime = applicationLifetime;
        _metrics = metrics;
        _idempotencyStore = idempotencyStore;
        _validator = validator;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        stoppingToken.ThrowIfCancellationRequested();

        InitializeConsumer();

        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void InitializeConsumer()
    {
        _channel = _connection.CreateChannel();
        _channel.BasicQos(0, _settings.PrefetchCount, global: false);
        RabbitMqTopology.EnsureQueue(_channel, _settings);

        _channel.CallbackException += (_, args) =>
        {
            _logger.LogCritical(args.Exception, "Channel callback exception; stopping application to preserve messages");
            _applicationLifetime.StopApplication();
        };
        _channel.ModelShutdown += (_, args) =>
        {
            _logger.LogCritical("Channel shutdown ({Reason}); stopping application to preserve messages", args.ReplyText);
            _applicationLifetime.StopApplication();
        };

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.Received += OnMessageAsync;
        _consumer.ConsumerCancelled += (_, __) =>
        {
            _logger.LogCritical("Consumer cancelled by broker; stopping application to preserve messages");
            _applicationLifetime.StopApplication();
            return Task.CompletedTask;
        };

        _channel.BasicConsume(_settings.QueueName, autoAck: false, consumer: _consumer);

        _logger.LogInformation("Player queue consumer started on {Queue}", _settings.QueueName);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        var channel = _channel;
        if (channel is null || !channel.IsOpen)
        {
            _logger.LogCritical("Channel is closed, stopping application to avoid dropping message {DeliveryTag}", args.DeliveryTag);
            _applicationLifetime.StopApplication();
            throw new InvalidOperationException("Consumer channel is closed.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        var cancellationToken = linkedCts.Token;
        using var activity = StartConsumeActivity(args);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _settings.QueueName);
        activity?.SetTag("player.deliveryTag", args.DeliveryTag);

        var stopwatch = Stopwatch.StartNew();
        var inFlightIncremented = false;
        try
        {
            var message = JsonSerializer.Deserialize<PlayerEnqueuedEvent>(args.Body.Span, SerializerOptions);

            if (message is null)
            {
                _logger.LogError("Received empty player event message; sending to dead-letter queue");
                activity?.SetStatus(ActivityStatusCode.Error, "deserialization_failed");
                stopwatch.Stop();
                var poison = new PlayerEnqueuedEvent { GameMode = "unknown", Region = "unknown" };
                _metrics.IncrementValidationFailure(poison, "consume", "deserialization_failed", _settings.QueueName);
                _metrics.IncrementDeadLetter(poison, "deserialization_failed", _settings.QueueName);
                _metrics.IncrementConsumeFailure(poison, "deserialization_failed", _settings.QueueName);
                _metrics.RecordConsumeDuration(poison, stopwatch.Elapsed.TotalMilliseconds, _settings.QueueName);
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            activity?.SetTag("player.id", message.PlayerId);
            activity?.SetTag("player.region", message.Region);
            activity?.SetTag("player.mode", message.GameMode);

            var validation = _validator.Validate(message);
            if (!validation.IsValid)
            {
                var errorMessage = string.Join("; ", validation.Errors);
                _logger.LogWarning(
                    "Validation failed for message {MessageId} ({PlayerId} {Region}/{Mode}): {Errors}",
                    message.MessageId,
                    message.PlayerId,
                    message.Region,
                    message.GameMode,
                    errorMessage);
                activity?.SetStatus(ActivityStatusCode.Error, "validation_failed");
                stopwatch.Stop();
                _metrics.IncrementValidationFailure(message, "consume", errorMessage, _settings.QueueName);
                _metrics.IncrementDeadLetter(message, "validation_failed", _settings.QueueName);
                _metrics.IncrementConsumeFailure(message, "validation_failed", _settings.QueueName);
                _metrics.RecordConsumeDuration(message, stopwatch.Elapsed.TotalMilliseconds, _settings.QueueName);
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await using var idempotencyLease = await _idempotencyStore
                .AcquireAsync(message.MessageId, cancellationToken)
                .ConfigureAwait(false);

            if (!idempotencyLease.ShouldProcess)
            {
                _logger.LogInformation(
                    "Skipping duplicate message {MessageId} for player {PlayerId} ({Region}/{Mode})",
                    message.MessageId,
                    message.PlayerId,
                    message.Region,
                    message.GameMode);
                channel.BasicAck(args.DeliveryTag, multiple: false);
                stopwatch.Stop();
                _metrics.IncrementConsumeSuccess(message, _settings.QueueName);
                _metrics.RecordConsumeDuration(message, stopwatch.Elapsed.TotalMilliseconds, _settings.QueueName);
                activity?.SetStatus(ActivityStatusCode.Ok, "duplicate");
                return;
            }

            _metrics.IncrementInFlight(_settings.QueueName);
            inFlightIncremented = true;
            var outcome = await TryProcessWithRetryAsync(message, activity, cancellationToken).ConfigureAwait(false);

            if (!outcome.Succeeded)
            {
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                stopwatch.Stop();
                var reason = outcome.FailureReason ?? "processing_failed";
                _metrics.IncrementDeadLetter(message, reason, _settings.QueueName);
                _metrics.IncrementConsumeFailure(message, reason, _settings.QueueName);
                _metrics.RecordConsumeDuration(message, stopwatch.Elapsed.TotalMilliseconds, _settings.QueueName);
                activity?.SetStatus(ActivityStatusCode.Error, reason);
                return;
            }

            await idempotencyLease.MarkProcessedAsync().ConfigureAwait(false);
            channel.BasicAck(args.DeliveryTag, multiple: false);
            stopwatch.Stop();
            _metrics.IncrementProcessed(message, _settings.QueueName);
            _metrics.IncrementConsumeSuccess(message, _settings.QueueName);
            _metrics.RecordConsumeDuration(message, stopwatch.Elapsed.TotalMilliseconds, _settings.QueueName);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Cancellation requested while processing message {DeliveryTag}", args.DeliveryTag);
            if (channel.IsOpen)
            {
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
            }
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            stopwatch.Stop();
            _metrics.IncrementConsumeFailure(
                new PlayerEnqueuedEvent { GameMode = "unknown", Region = "unknown" },
                "canceled",
                _settings.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled processing failure for message {DeliveryTag}; stopping application", args.DeliveryTag);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _applicationLifetime.StopApplication();
            throw;
        }
        finally
        {
            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }
            if (inFlightIncremented)
            {
                _metrics.DecrementInFlight(_settings.QueueName);
            }
        }
    }

    private static Activity? StartConsumeActivity(BasicDeliverEventArgs args)
    {
        var headers = args.BasicProperties?.Headers ?? new Dictionary<string, object?>();
        var carrier = new Dictionary<string, string?>();

        foreach (var (key, value) in headers)
        {
            switch (value)
            {
                case byte[] bytes:
                    carrier[key] = Encoding.UTF8.GetString(bytes);
                    break;
                case string str:
                    carrier[key] = str;
                    break;
            }
        }

        var propagationContext = Tracing.Propagator.Extract(
            default,
            carrier,
            static (dict, key) => dict.TryGetValue(key, out var value) && value is not null
                ? new[] { value }
                : Array.Empty<string>());

        return Tracing.ActivitySource.StartActivity(
            "rabbitmq.consume",
            ActivityKind.Consumer,
            propagationContext.ActivityContext);
    }

    private async Task<ProcessingOutcome> TryProcessWithRetryAsync(
        PlayerEnqueuedEvent message,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var retryDelay = TimeSpan.FromSeconds(_settings.RetryDelaySeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;

            try
            {
                await _processor.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
                return ProcessingOutcome.Success();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _settings.MaxRetryAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Processing attempt {Attempt} failed for player {PlayerId}, retrying after {Delay}ms",
                    attempt,
                    message.PlayerId,
                    retryDelay.TotalMilliseconds);

                activity?.AddEvent(new ActivityEvent("retrying", tags: new ActivityTagsCollection
                {
                    { "retry.attempt", attempt },
                    { "player.id", message.PlayerId }
                }));
                _metrics.IncrementProcessingRetry(message, _settings.QueueName);

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Processing failed for player {PlayerId} after {Attempts} attempts; sending to dead-letter queue",
                    message.PlayerId,
                    attempt);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProcessingOutcome.Failure(ex.GetType().Name);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private sealed record ProcessingOutcome(bool Succeeded, string? FailureReason)
    {
        public static ProcessingOutcome Success() => new(true, null);
        public static ProcessingOutcome Failure(string? reason) => new(false, reason);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Close();
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        if (_consumer is not null)
        {
            _consumer.Received -= OnMessageAsync;
        }

        _channel?.Dispose();
        base.Dispose();
    }
}
