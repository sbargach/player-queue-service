using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;
using PlayerQueueService.Api.Services;
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
    }
}
