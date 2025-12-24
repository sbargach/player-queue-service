using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shouldly;

namespace PlayerQueueService.Api.Tests;

public class MatchResultPublisherTests
{
    private IRabbitMqConnection _connection = null!;
    private IModel _channel = null!;
    private IBasicProperties _properties = null!;
    private RabbitMQSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = Substitute.For<IRabbitMqConnection>();
        _channel = Substitute.For<IModel>();
        _properties = Substitute.For<IBasicProperties>();
        _settings = new RabbitMQSettings
        {
            ExchangeName = "player-queue",
            QueueName = "player-queue.enqueued",
            RoutingKey = "player.queue.enqueued",
            MatchResultsExchangeName = "player-match",
            MatchResultsQueueName = "player-match.formed",
            MatchResultsRoutingKey = "player.match.formed",
            PublishConfirmTimeoutSeconds = 1,
            RetryDelaySeconds = 1
        };

        _connection.CreateChannel().Returns(_channel);
        _channel.CreateBasicProperties().Returns(_properties);
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(true);
    }

    [Test]
    public async Task PublishAsync_RetriesUntilCancelled_WhenConfirmationTimesOut()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(false);
        var publisher = new MatchResultPublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<MatchResultPublisher>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(
            () => publisher.PublishAsync(BuildMatch(), cts.Token));

        _channel.Received().ConfirmSelect();
    }

    [Test]
    public async Task PublishAsync_ThrowsWhenMessageIsReturned()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());
        var publisher = new MatchResultPublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<MatchResultPublisher>.Instance);

        await Should.ThrowAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(BuildMatch()));

        _channel.Received(1).BasicPublish(
            _settings.MatchResultsExchangeName,
            _settings.MatchResultsRoutingKey,
            true,
            _properties,
            Arg.Any<ReadOnlyMemory<byte>>());
    }

    private static MatchResult BuildMatch()
    {
        var player = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            SkillRating = 1200,
            RequestedAt = DateTimeOffset.UtcNow,
            Region = "eu",
            GameMode = "trios"
        };

        return new MatchResult(
            Guid.NewGuid(),
            new[] { player },
            player.Region,
            player.GameMode,
            DateTimeOffset.UtcNow,
            player.SkillRating);
    }

    private Func<CallInfo, bool> InvokeBasicReturn()
    {
        return _ =>
        {
            _channel.BasicReturn += Raise.EventWith(
                _channel,
                new BasicReturnEventArgs
                {
                    Exchange = _settings.MatchResultsExchangeName,
                    RoutingKey = _settings.MatchResultsRoutingKey,
                    ReplyCode = 312,
                    ReplyText = "NO_ROUTE"
                });
            return true;
        };
    }
}
