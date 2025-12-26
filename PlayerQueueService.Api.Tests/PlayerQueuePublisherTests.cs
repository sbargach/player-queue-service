using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shouldly;

namespace PlayerQueueService.Api.Tests;

public class PlayerQueuePublisherTests
{
    private IRabbitMqConnection _connection = null!;
    private IModel _channel = null!;
    private IBasicProperties _properties = null!;
    private RabbitMQSettings _settings = null!;
    private IMetricsProvider _metrics = null!;
    private IPlayerEnqueuedEventValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = Substitute.For<IRabbitMqConnection>();
        _channel = Substitute.For<IModel>();
        _properties = Substitute.For<IBasicProperties>();
        _metrics = Substitute.For<IMetricsProvider>();
        _validator = Substitute.For<IPlayerEnqueuedEventValidator>();
        _validator.Validate(Arg.Any<PlayerEnqueuedEvent>()).Returns(ValidationOutcome.Success());
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

    [Test]
    public async Task PublishAsync_RetriesUntilCancelled_WhenConfirmationTimesOut()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(false);

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics,
            _validator);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(
            () => publisher.PublishAsync(ValidEvent(), cts.Token));

        _channel.Received().ConfirmSelect();
    }

    [Test]
    public async Task PublishAsync_ThrowsWhenMessageIsReturned()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics,
            _validator);

        await Should.ThrowAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(ValidEvent()));

        _channel.Received(1).BasicPublish(
            _settings.ExchangeName,
            _settings.RoutingKey,
            true,
            _properties,
            Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Test]
    public async Task PublishAsync_DoesNotRetryOnBrokerReturn()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics,
            _validator);

        await Should.ThrowAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(ValidEvent()));

        _connection.Received(1).CreateChannel();
    }

    [Test]
    public async Task PublishAsync_RecordsMetricsOnSuccess()
    {
        var playerEvent = new PlayerEnqueuedEvent { GameMode = "trios", Region = "eu" };
        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics,
            _validator);

        await publisher.PublishAsync(playerEvent);

        _metrics.Received(1).IncrementPublishAttempt(playerEvent);
        _metrics.Received(1).IncrementPublishSuccess(playerEvent);
        _metrics.Received(1).RecordPublishDuration(playerEvent, Arg.Any<double>());
        _metrics.DidNotReceive().IncrementPublishFailure(Arg.Any<PlayerEnqueuedEvent>(), Arg.Any<string>());
    }

    [Test]
    public async Task PublishAsync_RecordsFailureMetricOnBrokerReturn()
    {
        _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(InvokeBasicReturn());
        var playerEvent = new PlayerEnqueuedEvent { GameMode = "ranked", Region = "na" };

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics,
            _validator);

        await Should.ThrowAsync<BrokerReturnedMessageException>(() => publisher.PublishAsync(playerEvent));

        _metrics.Received(1).IncrementPublishAttempt(playerEvent);
        _metrics.Received(1).IncrementPublishFailure(playerEvent, nameof(BrokerReturnedMessageException));
    }

    [Test]
    public async Task PublishAsync_ThrowsWhenValidationFails()
    {
        var playerEvent = ValidEvent();
        _validator.Validate(playerEvent).Returns(ValidationOutcome.Failure(new[] { "Region is required." }));

        var publisher = new PlayerQueuePublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<PlayerQueuePublisher>.Instance,
            _metrics,
            _validator);

        await Should.ThrowAsync<ValidationException>(() => publisher.PublishAsync(playerEvent));

        _metrics.Received(1).IncrementValidationFailure(playerEvent, "publish", Arg.Any<string>(), _settings.QueueName);
        _metrics.DidNotReceive().IncrementPublishAttempt(Arg.Any<PlayerEnqueuedEvent>());
    }

    private static PlayerEnqueuedEvent ValidEvent() => new()
    {
        PlayerId = Guid.NewGuid(),
        Region = "eu",
        GameMode = "trios",
        SkillRating = 1200
    };

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
