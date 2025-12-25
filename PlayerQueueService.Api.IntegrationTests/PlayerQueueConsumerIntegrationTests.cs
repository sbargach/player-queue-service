using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Messaging.Publishing;
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
    private RabbitMQSettings _settings = new()
    {
        QueueName = "player-queue.enqueued",
        ExchangeName = "player-queue",
        RoutingKey = "player.queue.enqueued",
        MatchResultsExchangeName = "player-match",
        MatchResultsQueueName = "player-match.formed",
        MatchResultsRoutingKey = "player.match.formed",
        PrefetchCount = 5
    };

    private TestRabbitMqConnection? _connection;
    private readonly IMetricsProvider _metrics = Substitute.For<IMetricsProvider>();
    private readonly CapturingMatchResultPublisher _publisher = new();
    private IMemoryCache? _memoryCache;
    private InMemoryIdempotencyStore? _idempotencyStore;
    private TestHostApplicationLifetime? _lifetime;
    private PlayerQueueConsumer? _consumer;

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

        await _connection!.DeliverAsync(playerOne);
        await _connection!.DeliverAsync(playerTwo);

        var match = await _publisher.MatchPublished.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(match);
        Assert.Equal(2, match!.Players.Count);
        Assert.Equal(playerOne.Region, match.Region);
        Assert.Equal(playerOne.GameMode, match.GameMode);
    }

    [Fact]
    public async Task DuplicateMessages_AreProcessedOnce()
    {
        var processor = new CountingProcessor();
        await StartConsumerAsync(processor);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "na",
            GameMode = "duos",
            SkillRating = 1500,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _connection!.DeliverAsync(playerEvent);
        await _connection!.DeliverAsync(playerEvent);
        await _connection!.DeliverAsync(playerEvent);

        await processor.WaitForFirstAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, processor.ProcessCount);
        Assert.Single(processor.ProcessedMessageIds);
        Assert.Contains(playerEvent.MessageId, processor.ProcessedMessageIds);
    }

    [Fact]
    public async Task ProcessingRetriesThenSucceeds_AcksOnce()
    {
        var processor = new FailingThenSucceedsProcessor(failuresBeforeSuccess: 2);
        var settings = _settings with { MaxRetryAttempts = 5, RetryDelaySeconds = 1 };
        await StartConsumerAsync(processor, settings);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "eu",
            GameMode = "quads",
            SkillRating = 1300,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _connection!.DeliverAsync(playerEvent);
        await processor.WaitForCallsAsync(3, TimeSpan.FromSeconds(5));

        Assert.Equal(3, processor.CallCount);
        Assert.Equal(1, _connection.AckCount);
        Assert.Equal(0, _connection.NackCount);
        Assert.False(_lifetime!.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task ProcessingFailsAfterMaxRetries_StopsApplicationWithoutAck()
    {
        var processor = new AlwaysFailProcessor();
        var settings = _settings with { MaxRetryAttempts = 2, RetryDelaySeconds = 1 };
        await StartConsumerAsync(processor, settings);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "apac",
            GameMode = "solos",
            SkillRating = 1100,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _connection!.DeliverAsync(playerEvent));

        Assert.True(_lifetime!.ApplicationStopping.IsCancellationRequested);
        Assert.NotNull(_connection);
        Assert.Equal(0, _connection.AckCount);
        Assert.Equal(0, _connection.NackCount);
        Assert.Equal(2, processor.ProcessCount);
    }

    [Fact]
    public async Task CancellationDuringProcessing_NacksMessage()
    {
        var processor = new BlockingProcessor();
        using var cts = new CancellationTokenSource();
        await StartConsumerAsync(processor, startToken: cts.Token);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "na",
            GameMode = "duos",
            SkillRating = 1500,
            RequestedAt = DateTimeOffset.UtcNow
        };

        var delivery = _connection!.DeliverAsync(playerEvent);
        await processor.WaitForStartAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await delivery;

        Assert.Equal(0, _connection.AckCount);
        Assert.Equal(1, _connection.NackCount);
        Assert.True(_connection.LastNackRequeue);
    }

    [Fact]
    public async Task IdempotencyEvicted_AllowsReplay()
    {
        var processor = new CountingProcessor();
        await StartConsumerAsync(processor);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "eu",
            GameMode = "trios",
            SkillRating = 1250,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _connection!.DeliverAsync(playerEvent);
        await processor.WaitForCallsAsync(1, TimeSpan.FromSeconds(3));

        _memoryCache!.Remove(playerEvent.MessageId); // simulate retention expiry

        await _connection!.DeliverAsync(playerEvent);
        await processor.WaitForCallsAsync(2, TimeSpan.FromSeconds(3));

        Assert.Equal(2, processor.ProcessCount);
        Assert.Equal(2, _connection.AckCount);
    }

    [Fact]
    public async Task DuplicateMessages_AreAckedWhenSkipped()
    {
        var processor = new CountingProcessor();
        await StartConsumerAsync(processor);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "na",
            GameMode = "quads",
            SkillRating = 1600,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _connection!.DeliverAsync(playerEvent);
        await _connection!.DeliverAsync(playerEvent);
        await _connection!.DeliverAsync(playerEvent);

        await processor.WaitForCallsAsync(1, TimeSpan.FromSeconds(3));

        Assert.Equal(1, processor.ProcessCount);
        Assert.Equal(3, _connection.AckCount);
        Assert.Equal(0, _connection.NackCount);
    }

    [Fact]
    public async Task TraceContextHeaders_AreAppliedToConsumeActivity()
    {
        var processor = new CountingProcessor();

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = traceParent
        };

        var collected = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Tracing.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => collected.Add(activity),
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        await StartConsumerAsync(processor);

        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "sa",
            GameMode = "duos",
            SkillRating = 1000,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _connection!.DeliverAsync(playerEvent, headers);
        await processor.WaitForCallsAsync(1, TimeSpan.FromSeconds(3));

        var consumeActivity = collected.FirstOrDefault(a => a.OperationName == "rabbitmq.consume");
        Assert.NotNull(consumeActivity);
        Assert.NotEqual(default, consumeActivity!.TraceId);
        Assert.Equal(_settings.QueueName, consumeActivity.GetTagItem("messaging.destination"));
        Assert.Equal(playerEvent.PlayerId, consumeActivity.GetTagItem("player.id"));
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

        _connection?.Dispose();
        _memoryCache?.Dispose();
    }

    private async Task StartConsumerAsync(
        IPlayerQueueProcessor? processor = null,
        RabbitMQSettings? settingsOverride = null,
        CancellationToken startToken = default)
    {
        _settings = settingsOverride ?? _settings;
        _connection = new TestRabbitMqConnection(_settings);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _idempotencyStore = new InMemoryIdempotencyStore(Options.Create(_settings), _memoryCache);

        processor ??= new PlayerQueueProcessor(
            new Matchmaker(
                Options.Create(new MatchmakingSettings { TeamSize = 2, MaxSkillDelta = 500, MaxQueueSeconds = 120 }),
                _metrics,
                NullLogger<Matchmaker>.Instance),
            _publisher,
            NullLogger<PlayerQueueProcessor>.Instance);

        _lifetime = new TestHostApplicationLifetime();
        _consumer = new PlayerQueueConsumer(
            _connection,
            processor,
            Options.Create(_settings),
            _lifetime,
            NullLogger<PlayerQueueConsumer>.Instance,
            _metrics,
            _idempotencyStore);

        await _consumer.StartAsync(startToken);
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

        public int AckCount { get; private set; }
        public int NackCount { get; private set; }
        public ulong LastAckDeliveryTag { get; private set; }
        public ulong LastNackDeliveryTag { get; private set; }
        public bool LastNackRequeue { get; private set; }

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

        public async Task DeliverAsync(PlayerEnqueuedEvent playerEvent, IDictionary<string, object?>? headers = null)
        {
            if (_consumer is null)
            {
                await _consumerReady.Task.WaitAsync(CancellationToken.None);
            }

            var body = JsonSerializer.SerializeToUtf8Bytes(playerEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var props = Substitute.For<IBasicProperties>();
            var headerMap = headers ?? new Dictionary<string, object?>();
            props.Headers.Returns(headerMap);
            props.MessageId.Returns(playerEvent.MessageId);

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
            channel.BasicConsume(default!, default, default!, default!, default!, default!, default!).ReturnsForAnyArgs(ci =>
            {
                _consumer = ci.ArgAt<IBasicConsumer>(6) as AsyncEventingBasicConsumer;
                if (_consumer is not null)
                {
                    _consumerReady.TrySetResult(_consumer);
                }
                return "consumer-tag";
            });
            channel.When(c => c.BasicAck(Arg.Any<ulong>(), Arg.Any<bool>()))
                .Do(ci =>
                {
                    AckCount++;
                    LastAckDeliveryTag = ci.ArgAt<ulong>(0);
                });
            channel.When(c => c.BasicNack(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>()))
                .Do(ci =>
                {
                    NackCount++;
                    LastNackDeliveryTag = ci.ArgAt<ulong>(0);
                    LastNackRequeue = ci.ArgAt<bool>(2);
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

    private sealed class CountingProcessor : IPlayerQueueProcessor
    {
        private readonly List<string> _processedMessageIds = new();
        private int _processedCount;

        public int ProcessCount => Volatile.Read(ref _processedCount);

        public IReadOnlyCollection<string> ProcessedMessageIds
        {
            get
            {
                lock (_processedMessageIds)
                {
                    return _processedMessageIds.ToList();
                }
            }
        }

        public Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _processedCount);

            lock (_processedMessageIds)
            {
                _processedMessageIds.Add(playerEvent.MessageId);
            }

            return Task.CompletedTask;
        }

        public Task WaitForFirstAsync(TimeSpan timeout) => WaitForCallsAsync(1, timeout);

        public async Task WaitForCallsAsync(int expectedCalls, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (Volatile.Read(ref _processedCount) < expectedCalls)
            {
                await Task.Delay(20, cts.Token);
            }
        }
    }

    private sealed class FailingThenSucceedsProcessor : IPlayerQueueProcessor
    {
        private readonly int _failuresBeforeSuccess;
        private int _callCount;
        private readonly TaskCompletionSource<bool> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FailingThenSucceedsProcessor(int failuresBeforeSuccess)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _callCount);
            if (attempt <= _failuresBeforeSuccess)
            {
                throw new InvalidOperationException("transient failure");
            }

            _completed.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task WaitForCallsAsync(int expectedCalls, TimeSpan timeout)
        {
            if (CallCount >= expectedCalls)
            {
                return Task.CompletedTask;
            }

            return _completed.Task.WaitAsync(timeout);
        }
    }

    private sealed class AlwaysFailProcessor : IPlayerQueueProcessor
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);
        public int ProcessCount => CallCount;

        public Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("permanent failure");
        }
    }

    private sealed class BlockingProcessor : IPlayerQueueProcessor
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult(true);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task WaitForStartAsync(TimeSpan timeout) => _started.Task.WaitAsync(timeout);
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
