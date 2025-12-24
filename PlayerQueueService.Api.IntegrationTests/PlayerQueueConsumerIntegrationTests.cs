using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace PlayerQueueService.Api.IntegrationTests;

public class PlayerQueueConsumerIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMQSettings _settings = new()
    {
        QueueName = "player-queue.enqueued",
        ExchangeName = "player-queue",
        RoutingKey = "player.queue.enqueued",
        MatchResultsExchangeName = "player-match",
        MatchResultsQueueName = "player-match.formed",
        MatchResultsRoutingKey = "player.match.formed",
        PrefetchCount = 5
    };

    private readonly TestRabbitMqConnection _connection;
    private readonly IMetricsProvider _metrics = Substitute.For<IMetricsProvider>();
    private readonly CapturingMatchResultPublisher _publisher = new();
    private PlayerQueueConsumer? _consumer;

    public PlayerQueueConsumerIntegrationTests()
    {
        _connection = new TestRabbitMqConnection(_settings);
    }

    [Fact]
    public async Task ConsumingQueue_FormsMatchAndPublishesResults()
    {
        await StartConsumerAsync();

        var playerOne = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "eu",
            GameMode = "trios",
            SkillRating = 1200,
            RequestedAt = DateTimeOffset.UtcNow
        };
        var playerTwo = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "eu",
            GameMode = "trios",
            SkillRating = 1210,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _connection.DeliverAsync(playerOne);
        await _connection.DeliverAsync(playerTwo);

        var match = await _publisher.MatchPublished.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(match);
        Assert.Equal(2, match!.Players.Count);
        Assert.Equal(playerOne.Region, match.Region);
        Assert.Equal(playerOne.GameMode, match.GameMode);
    }

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_consumer is not null)
        {
            await _consumer.StopAsync(CancellationToken.None);
        }
    }

    private async Task StartConsumerAsync()
    {
        var matchmaker = new Matchmaker(
            Options.Create(new MatchmakingSettings { TeamSize = 2, MaxSkillDelta = 500, MaxQueueSeconds = 120 }),
            _metrics,
            NullLogger<Matchmaker>.Instance);

        var processor = new PlayerQueueProcessor(
            matchmaker,
            _publisher,
            NullLogger<PlayerQueueProcessor>.Instance);

        _consumer = new PlayerQueueConsumer(
            _connection,
            processor,
            Options.Create(_settings),
            new TestHostApplicationLifetime(),
            NullLogger<PlayerQueueConsumer>.Instance,
            _metrics);

        await _consumer.StartAsync(CancellationToken.None);
        await _connection.WaitForConsumerAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class TestRabbitMqConnection : IRabbitMqConnection
    {
        private readonly RabbitMQSettings _settings;
        private readonly IModel _channel = Substitute.For<IModel>();
        private AsyncEventingBasicConsumer? _consumer;
        private readonly TaskCompletionSource<AsyncEventingBasicConsumer> _consumerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestRabbitMqConnection(RabbitMQSettings settings)
        {
            _settings = settings;
            ConfigureConsumerChannel(_channel);
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IModel CreateChannel() => _channel;

        public void Dispose()
        {
        }

        public async Task WaitForConsumerAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _consumerReady.Task.WaitAsync(cts.Token);
        }

        public async Task DeliverAsync(PlayerEnqueuedEvent playerEvent)
        {
            if (_consumer is null)
            {
                await _consumerReady.Task.WaitAsync(CancellationToken.None);
            }

            var body = JsonSerializer.SerializeToUtf8Bytes(playerEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var props = Substitute.For<IBasicProperties>();
            props.Headers = new Dictionary<string, object?>();

            await _consumer!.HandleBasicDeliver(
                consumerTag: "consumer-tag",
                deliveryTag: (ulong)Random.Shared.Next(1, int.MaxValue),
                redelivered: false,
                exchange: _settings.ExchangeName,
                routingKey: _settings.RoutingKey,
                properties: props,
                body: body);
        }

        private void ConfigureConsumerChannel(IModel channel)
        {
            channel.IsOpen.Returns(true);
            channel.CreateBasicProperties().Returns(Substitute.For<IBasicProperties>());
            channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(true);
            channel.ExchangeDeclare(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>());
            channel.QueueDeclare(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>()).Returns(new QueueDeclareOk("queue", 0, 0));
            channel.QueueBind(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IDictionary<string, object>>());
            channel.BasicConsume(default!, default, default!).ReturnsForAnyArgs(ci =>
            {
                _consumer = ci.ArgAt<IBasicConsumer>(2) as AsyncEventingBasicConsumer;
                if (_consumer is not null)
                {
                    _consumerReady.TrySetResult(_consumer);
                }
                return "consumer-tag";
            });
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class CapturingMatchResultPublisher : IMatchResultPublisher
    {
        private readonly TaskCompletionSource<MatchResult> _published = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishAsync(MatchResult match, CancellationToken cancellationToken = default)
        {
            _published.TrySetResult(match);
            return Task.CompletedTask;
        }

        public Task<MatchResult> MatchPublished => _published.Task;
    }
}
