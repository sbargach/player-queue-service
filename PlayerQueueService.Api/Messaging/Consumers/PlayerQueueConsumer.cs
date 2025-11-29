using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PlayerQueueService.Api.Messaging.Consumers;

public sealed class PlayerQueueConsumer : BackgroundService
{
    private readonly IRabbitMqConnection _connection;
    private readonly IPlayerQueueProcessor _processor;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<PlayerQueueConsumer> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private CancellationToken _stoppingToken;
    private IModel? _channel;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private AsyncEventingBasicConsumer? _consumer;

    public PlayerQueueConsumer(
        IRabbitMqConnection connection,
        IPlayerQueueProcessor processor,
        IOptions<RabbitMqOptions> options,
        IHostApplicationLifetime applicationLifetime,
        ILogger<PlayerQueueConsumer> logger)
    {
        _connection = connection;
        _processor = processor;
        _logger = logger;
        _options = options.Value;
        _applicationLifetime = applicationLifetime;
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
        _channel.BasicQos(0, _options.PrefetchCount, global: false);
        RabbitMqTopology.EnsureQueue(_channel, _options);

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

        _channel.BasicConsume(_options.QueueName, autoAck: false, consumer: _consumer);

        _logger.LogInformation("Player queue consumer started on {Queue}", _options.QueueName);
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

        try
        {
            var message = JsonSerializer.Deserialize<PlayerEnqueuedEvent>(args.Body.Span, SerializerOptions);

            if (message is null)
            {
                _logger.LogCritical("Received empty player event message; stopping application to preserve delivery");
                _applicationLifetime.StopApplication();
                throw new InvalidOperationException("Empty player event payload.");
            }

            await TryProcessWithRetryAsync(message, cancellationToken).ConfigureAwait(false);

            channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Cancellation requested while processing message {DeliveryTag}", args.DeliveryTag);
            if (channel.IsOpen)
            {
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled processing failure for message {DeliveryTag}; stopping application", args.DeliveryTag);
            _applicationLifetime.StopApplication();
            throw;
        }
    }

    private async Task TryProcessWithRetryAsync(
        PlayerEnqueuedEvent message,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var retryDelay = TimeSpan.FromSeconds(_options.RetryDelaySeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;

            try
            {
                await _processor.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.MaxRetryAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Processing attempt {Attempt} failed for player {PlayerId}, retrying after {Delay}ms",
                    attempt,
                    message.PlayerId,
                    retryDelay.TotalMilliseconds);

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
            catch (Exception ex)
            {
                _logger.LogCritical(
                    ex,
                    "Processing failed for player {PlayerId} after {Attempts} attempts; stopping application to preserve message",
                    message.PlayerId,
                    attempt);
                _applicationLifetime.StopApplication();
                throw;
            }
        }

        throw new OperationCanceledException(cancellationToken);
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
