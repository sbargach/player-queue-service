using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PlayerQueueService.Api.Tests;

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

        var published = await _connection.MatchResultPublished.WaitAsync(TimeSpan.FromSeconds(2));
        var matchEvent = JsonSerializer.Deserialize<MatchFormedEvent>(published.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(matchEvent);
        Assert.Equal(_settings.MatchResultsExchangeName, published.Exchange);
        Assert.Equal(_settings.MatchResultsRoutingKey, published.RoutingKey);
        Assert.Equal(2, matchEvent!.Players.Count);
        Assert.Equal(playerOne.Region, matchEvent.Region);
        Assert.Equal(playerOne.GameMode, matchEvent.GameMode);
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

        var matchResultPublisher = new MatchResultPublisher(
            _connection,
            Options.Create(_settings),
            NullLogger<MatchResultPublisher>.Instance);

        var processor = new PlayerQueueProcessor(
            matchmaker,
            matchResultPublisher,
            NullLogger<PlayerQueueProcessor>.Instance);

        _consumer = new PlayerQueueConsumer(
            _connection,
            processor,
            Options.Create(_settings),
            new TestHostApplicationLifetime(),
            NullLogger<PlayerQueueConsumer>.Instance,
            _metrics);

        await _consumer.StartAsync(CancellationToken.None);
        await _connection.WaitForConsumerAsync(TimeSpan.FromSeconds(1));
    }

    private sealed class TestRabbitMqConnection : IRabbitMqConnection
    {
        private readonly RabbitMQSettings _settings;
        private readonly IModel _channel = Substitute.For<IModel>();
        private AsyncEventingBasicConsumer? _consumer;
        private readonly TaskCompletionSource<AsyncEventingBasicConsumer> _consumerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<PublishCapture> _publishReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestRabbitMqConnection(RabbitMQSettings settings)
        {
            _settings = settings;
            _channel.IsOpen.Returns(true);
            _channel.CreateBasicProperties().Returns(new RabbitMQ.Client.Framing.BasicProperties());
            _channel.WaitForConfirms(Arg.Any<TimeSpan>()).Returns(true);
            _channel.ExchangeDeclare(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>()).Returns(new ExchangeDeclareOk());
            _channel.QueueDeclare(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>()).Returns(new QueueDeclareOk("queue", 0, 0));
            _channel.QueueBind(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IDictionary<string, object>>());
            _channel.BasicConsume(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IBasicConsumer>()).Returns(ci =>
            {
                _consumer = ci.Arg<IBasicConsumer>() as AsyncEventingBasicConsumer;
                if (_consumer is not null)
                {
                    _consumerReady.TrySetResult(_consumer);
                }
                return "consumer-tag";
            });

            _channel.When(c => c.BasicPublish(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<IBasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>()))
                .Do(ci =>
                {
                    var capture = new PublishCapture(
                        ci.ArgAt<string>(0),
                        ci.ArgAt<string>(1),
                        ci.ArgAt<ReadOnlyMemory<byte>>(4).ToArray());
                    _publishReady.TrySetResult(capture);
                });
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
            var props = new RabbitMQ.Client.Framing.BasicProperties { Headers = new Dictionary<string, object?>() };

            await _consumer!.HandleBasicDeliver(
                consumerTag: "consumer-tag",
                deliveryTag: (ulong)Random.Shared.Next(1, int.MaxValue),
                redelivered: false,
                exchange: _settings.ExchangeName,
                routingKey: _settings.RoutingKey,
                basicProperties: props,
                body: body);
        }

        public Task<PublishCapture> MatchResultPublished => _publishReady.Task;
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed record PublishCapture(string Exchange, string RoutingKey, byte[] Body);
}
