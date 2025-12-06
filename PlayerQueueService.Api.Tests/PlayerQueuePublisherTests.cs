using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PlayerQueueService.Api.Tests;

public class PlayerQueuePublisherTests
{
    private readonly IRabbitMqConnection _connection;
    private readonly IModel _channel;
    private readonly IBasicProperties _properties;
    private readonly RabbitMQSettings _settings;
    private readonly IMetricsProvider _metrics;

    public PlayerQueuePublisherTests()
    {
        _connection = Substitute.For<IRabbitMqConnection>();
        _channel = Substitute.For<IModel>();
        _properties = Substitute.For<IBasicProperties>();
        _metrics = Substitute.For<IMetricsProvider>();
        _settings = new RabbitMQSettings
        {
            ExchangeName = "player-queue",
            QueueName = "player-queue.enqueued",
            RoutingKey = "player.queue.enqueued",
            PublishConfirmTimeoutSeconds = 1,
            RetryDelaySeconds = 1
        };

        _connection.CreateChannel().Returns(_channel);
        _channel.CreateBasicProperties().Returns(_properties);
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(true);
    }

    [Fact]
    public async Task PublishAsync_RetriesUntilCancelled_WhenConfirmationTimesOut()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(false);

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => publisher.PublishAsync(new PlayerEnqueuedEvent(), cts.Token));

        _channel.Received().ConfirmSelect();
    }

    [Fact]
    public async Task PublishAsync_ThrowsWhenMessageIsReturned()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics);

        await Assert.ThrowsAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(new PlayerEnqueuedEvent()));

        _channel.Received(1).BasicPublish(
            _settings.ExchangeName,
            _settings.RoutingKey,
            true,
            _properties,
            Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Fact]
    public async Task PublishAsync_DoesNotRetryOnBrokerReturn()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics);

        await Assert.ThrowsAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(new PlayerEnqueuedEvent()));

        _connection.Received(1).CreateChannel();
    }

    [Fact]
    public async Task PublishAsync_RecordsMetricsOnSuccess()
    {
        var playerEvent = new PlayerEnqueuedEvent { GameMode = "trios", Region = "eu" };
        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics);

        await publisher.PublishAsync(playerEvent);

        _metrics.Received(1).IncrementPublishAttempt(playerEvent);
        _metrics.Received(1).IncrementPublishSuccess(playerEvent);
        _metrics.Received(1).RecordPublishDuration(playerEvent, Arg.Any<double>());
        _metrics.DidNotReceive().IncrementPublishFailure(Arg.Any<PlayerEnqueuedEvent>(), Arg.Any<string>());
    }

    [Fact]
    public async Task PublishAsync_RecordsFailureMetricOnBrokerReturn()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());
        var playerEvent = new PlayerEnqueuedEvent { GameMode = "ranked", Region = "na" };

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics);

        await Assert.ThrowsAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(playerEvent));

        _metrics.Received(1).IncrementPublishAttempt(playerEvent);
        _metrics.Received(1).IncrementPublishFailure(playerEvent, nameof(BrokerReturnedMessageException));
    }

    private Func<CallInfo, bool> InvokeBasicReturn()
    {
        return _ =>
        {
            _channel.BasicReturn += Raise.EventWith(
                _channel,
                new BasicReturnEventArgs
                {
                    Exchange = _settings.ExchangeName,
                    RoutingKey = _settings.RoutingKey,
                    ReplyCode = 312,
                    ReplyText = "NO_ROUTE"
                });
            return true;
        };
    }
}
