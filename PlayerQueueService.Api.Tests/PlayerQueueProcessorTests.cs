using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using Shouldly;

namespace PlayerQueueService.Api.Tests;

public class PlayerQueueProcessorTests
{
    [Test]
    public async Task ProcessAsync_CompletesWhenNotCancelled()
    {
        var matchmaker = Substitute.For<IMatchmaker>();
        matchmaker.EnqueueAsync(Arg.Any<PlayerEnqueuedEvent>(), Arg.Any<CancellationToken>())
            .Returns((MatchResult?)null);
        var publisher = Substitute.For<IMatchResultPublisher>();
        var processor = new PlayerQueueProcessor(matchmaker, publisher, NullLogger<PlayerQueueProcessor>.Instance);
        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "na",
            GameMode = "duos",
            RequestedAt = DateTimeOffset.UtcNow
        };

        await processor.ProcessAsync(playerEvent);

        await matchmaker.Received(1).EnqueueAsync(playerEvent, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_ThrowsWhenCancellationRequested()
    {
        var matchmaker = Substitute.For<IMatchmaker>();
        var publisher = Substitute.For<IMatchResultPublisher>();
        var processor = new PlayerQueueProcessor(matchmaker, publisher, NullLogger<PlayerQueueProcessor>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => processor.ProcessAsync(new PlayerEnqueuedEvent(), cts.Token));
    }

    [Test]
    public async Task ProcessAsync_LogsWhenMatchIsFormed()
    {
        var matchmaker = Substitute.For<IMatchmaker>();
        var publisher = Substitute.For<IMatchResultPublisher>();
        var player = new PlayerEnqueuedEvent { PlayerId = Guid.NewGuid(), Region = "eu", GameMode = "trios", SkillRating = 1200 };
        var match = new MatchResult(Guid.NewGuid(), new[] { player }, player.Region, player.GameMode, DateTimeOffset.UtcNow, player.SkillRating);
        matchmaker.EnqueueAsync(player, Arg.Any<CancellationToken>()).Returns(match);

        var processor = new PlayerQueueProcessor(matchmaker, publisher, NullLogger<PlayerQueueProcessor>.Instance);

        await processor.ProcessAsync(player);

        await matchmaker.Received(1).EnqueueAsync(player, Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(match, Arg.Any<CancellationToken>());
        matchmaker.Received(1).ConfirmPublished(match);
    }

    [Test]
    public async Task ProcessAsync_RetriesTheSameMatchAfterPublishFailure()
    {
        var metrics = Substitute.For<IMetricsProvider>();
        var matchmaker = new Matchmaker(
            Options.Create(new MatchmakingSettings { TeamSize = 2, MaxSkillDelta = 100, MaxQueueSeconds = 60 }),
            metrics,
            NullLogger<Matchmaker>.Instance);
        var publisher = Substitute.For<IMatchResultPublisher>();
        var attempts = new List<MatchResult>();
        publisher.PublishAsync(Arg.Any<MatchResult>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                attempts.Add(call.Arg<MatchResult>());
                return attempts.Count == 1
                    ? Task.FromException(new InvalidOperationException("publish failed"))
                    : Task.CompletedTask;
            });

        var processor = new PlayerQueueProcessor(matchmaker, publisher, NullLogger<PlayerQueueProcessor>.Instance);
        var first = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "eu",
            GameMode = "duos",
            SkillRating = 1200
        };
        var second = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "eu",
            GameMode = "duos",
            SkillRating = 1210
        };

        await processor.ProcessAsync(first);
        await Should.ThrowAsync<InvalidOperationException>(() => processor.ProcessAsync(second));
        await processor.ProcessAsync(second);

        attempts.Count.ShouldBe(2);
        attempts[0].MatchId.ShouldBe(attempts[1].MatchId);
        attempts[1].Players.Count.ShouldBe(2);
        metrics.Received(1).IncrementMatchFormed(attempts[1]);
    }
}
