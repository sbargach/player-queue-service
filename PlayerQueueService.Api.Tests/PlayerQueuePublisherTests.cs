using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PlayerQueueService.Api.Tests;

public class PlayerQueuePublisherTests
{
    private readonly IRabbitMqConnection _connection;
    private readonly IModel _channel;
    private readonly IBasicProperties _properties;
    private readonly RabbitMqOptions _options;

    public PlayerQueuePublisherTests()
    {
        _connection = Substitute.For<IRabbitMqConnection>();
        _channel = Substitute.For<IModel>();
        _properties = Substitute.For<IBasicProperties>();
        _options = new RabbitMqOptions
        {
            ExchangeName = "player-queue",
            QueueName = "player-queue.enqueued",
            RoutingKey = "player.queue.enqueued",
            PublishConfirmTimeoutSeconds = 1
        };

        _connection.CreateChannel().Returns(_channel);
        _channel.CreateBasicProperties().Returns(_properties);
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(true);
    }

    [Fact]
    public async Task PublishAsync_ThrowsWhenConfirmationTimesOut()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(false);

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_options),
            NullLogger<PlayerQueuePublisher>.Instance);

        await Assert.ThrowsAsync<TimeoutException>(() => publisher.PublishAsync(new PlayerEnqueuedEvent()));
        _channel.Received(1).ConfirmSelect();
    }

    [Fact]
    public async Task PublishAsync_ThrowsWhenMessageIsReturned()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_options),
            NullLogger<PlayerQueuePublisher>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new PlayerEnqueuedEvent()));

        _channel.Received(1).BasicPublish(
            _options.ExchangeName,
            _options.RoutingKey,
            true,
            _properties,
            Arg.Any<ReadOnlyMemory<byte>>());
    }

    private Func<CallInfo, bool> InvokeBasicReturn()
    {
        return _ =>
        {
            _channel.BasicReturn += Raise.EventWith(
                _channel,
                new BasicReturnEventArgs
                {
                    Exchange = _options.ExchangeName,
                    RoutingKey = _options.RoutingKey,
                    ReplyCode = 312,
                    ReplyText = "NO_ROUTE"
                });
            return true;
        };
    }
}
