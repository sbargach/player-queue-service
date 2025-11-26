using System.Text.Json;
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
    private IModel? _channel;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public PlayerQueueConsumer(
        IRabbitMqConnection connection,
        IPlayerQueueProcessor processor,
        IOptions<RabbitMqOptions> options,
        ILogger<PlayerQueueConsumer> logger)
    {
        _connection = connection;
        _processor = processor;
        _logger = logger;
        _options = options.Value;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        _channel = _connection.CreateChannel();
        _channel.BasicQos(0, _options.PrefetchCount, global: false);
        RabbitMqTopology.EnsureQueue(_channel, _options);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageAsync;
        _channel.BasicConsume(_options.QueueName, autoAck: false, consumer: consumer);

        _logger.LogInformation("Player queue consumer started on {Queue}", _options.QueueName);

        return Task.CompletedTask;
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        var channel = _channel;
        if (channel is null || !channel.IsOpen)
        {
            _logger.LogWarning("Channel is closed, dropping message {DeliveryTag}", args.DeliveryTag);
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<PlayerEnqueuedEvent>(args.Body.Span, SerializerOptions);

            if (message is null)
            {
                _logger.LogWarning("Received empty player event message, discarding");
                channel.BasicNack(args.DeliveryTag, false, requeue: false);
                return;
            }

            await _processor.ProcessAsync(message, CancellationToken.None);

            channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {DeliveryTag}", args.DeliveryTag);
            channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Close();
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}
