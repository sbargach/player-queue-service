using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using NUnit.Framework;
using Shouldly;

namespace PlayerQueueService.Api.Tests;

public class MatchmakerTests
{
    private readonly IMetricsProvider _metrics = Substitute.For<IMetricsProvider>();
    private readonly MatchmakingSettings _settings = new()
    {
        TeamSize = 2,
        MaxSkillDelta = 100,
        MaxQueueSeconds = 60
    };

    [Test]
    public async Task EnqueueAsync_FormsMatchWhenEnoughPlayersWithinDelta()
    {
        var matchmaker = CreateMatchmaker();
        var playerOne = BuildPlayer(1100);
        var playerTwo = BuildPlayer(1120);

        var result = await matchmaker.EnqueueAsync(playerOne);
        result.ShouldBeNull();

        result = await matchmaker.EnqueueAsync(playerTwo);

        result.ShouldNotBeNull();
        result!.Players.Count.ShouldBe(2);
        matchmaker.ConfirmPublished(result);
        _metrics.Received().IncrementMatchFormed(result);
        _metrics.Received().RecordQueueWait(result);
    }

    [Test]
    public async Task EnqueueAsync_DoesNotMatchWhenSkillGapTooLarge()
    {
        var matchmaker = CreateMatchmaker();
        var playerOne = BuildPlayer(900);
        var playerTwo = BuildPlayer(1200);

        var result = await matchmaker.EnqueueAsync(playerOne);
        result.ShouldBeNull();

        result = await matchmaker.EnqueueAsync(playerTwo);

        result.ShouldBeNull();
        _metrics.DidNotReceive().IncrementMatchFormed(Arg.Any<Models.Matchmaking.MatchResult>());
    }

    [Test]
    public async Task EnqueueAsync_KeepsRegionAndModeBucketsSeparate()
    {
        var matchmaker = CreateMatchmaker();
        var first = BuildPlayer(1100) with { Region = "EU:ranked", GameMode = "duos" };
        var otherMode = BuildPlayer(1110) with { Region = "eu", GameMode = "ranked:duos" };
        var teammate = BuildPlayer(1120) with { Region = "eu:ranked", GameMode = "duos" };

        (await matchmaker.EnqueueAsync(first)).ShouldBeNull();
        (await matchmaker.EnqueueAsync(otherMode)).ShouldBeNull();

        var match = await matchmaker.EnqueueAsync(teammate);

        match.ShouldNotBeNull();
        match!.Players.Select(player => player.MessageId)
            .ShouldBe(new[] { first.MessageId, teammate.MessageId }, ignoreOrder: true);
    }

    [Test]
    public async Task EnqueueAsync_DropsExpiredPlayersBeforeMatching()
    {
        var matchmaker = CreateMatchmaker();
        var stalePlayer = BuildPlayer(1000) with { RequestedAt = DateTimeOffset.UtcNow.AddSeconds(-_settings.MaxQueueSeconds - 5) };
        var recentPlayerOne = BuildPlayer(1010);
        var recentPlayerTwo = BuildPlayer(1020);

        await matchmaker.EnqueueAsync(stalePlayer);
        await matchmaker.EnqueueAsync(recentPlayerOne);
        var result = await matchmaker.EnqueueAsync(recentPlayerTwo);

        result.ShouldNotBeNull();
        result!.Players.ShouldNotContain(stalePlayer);
    }

    [Test]
    public async Task PublishFailure_RestoresWaitingPlayersForRetry()
    {
        _metrics.ClearReceivedCalls();
        var matchmaker = CreateMatchmaker();
        var first = BuildPlayer(1100);
        var second = BuildPlayer(1120);

        await matchmaker.EnqueueAsync(first);
        var original = await matchmaker.EnqueueAsync(second);
        original.ShouldNotBeNull();

        matchmaker.RestoreAfterPublishFailure(original, second.MessageId);
        var retry = await matchmaker.EnqueueAsync(second);

        retry.ShouldNotBeNull();
        retry!.MatchId.ShouldBe(original.MatchId);
        retry.Players.Select(player => player.MessageId)
            .ShouldBe(original.Players.Select(player => player.MessageId), ignoreOrder: true);
        _metrics.DidNotReceive().IncrementMatchFormed(Arg.Any<Models.Matchmaking.MatchResult>());
    }

    private Matchmaker CreateMatchmaker() =>
        new(Options.Create(_settings), _metrics, NullLogger<Matchmaker>.Instance);

    private static PlayerEnqueuedEvent BuildPlayer(int skill) =>
        new()
        {
            PlayerId = Guid.NewGuid(),
            SkillRating = skill,
            GameMode = "duos",
            Region = "eu",
            RequestedAt = DateTimeOffset.UtcNow
        };
}
