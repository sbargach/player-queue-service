using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;

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

    [Fact]
    public async Task EnqueueAsync_FormsMatchWhenEnoughPlayersWithinDelta()
    {
        var matchmaker = CreateMatchmaker();
        var playerOne = BuildPlayer(1100);
        var playerTwo = BuildPlayer(1120);

        var result = await matchmaker.EnqueueAsync(playerOne);
        Assert.Null(result);

        result = await matchmaker.EnqueueAsync(playerTwo);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Players.Count);
        _metrics.Received().IncrementMatchFormed(result);
        _metrics.Received().RecordQueueWait(result);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotMatchWhenSkillGapTooLarge()
    {
        var matchmaker = CreateMatchmaker();
        var playerOne = BuildPlayer(900);
        var playerTwo = BuildPlayer(1200);

        var result = await matchmaker.EnqueueAsync(playerOne);
        Assert.Null(result);

        result = await matchmaker.EnqueueAsync(playerTwo);

        Assert.Null(result);
        _metrics.DidNotReceive().IncrementMatchFormed(Arg.Any<Models.Matchmaking.MatchResult>());
    }

    [Fact]
    public async Task EnqueueAsync_DropsExpiredPlayersBeforeMatching()
    {
        var matchmaker = CreateMatchmaker();
        var stalePlayer = BuildPlayer(1000) with { RequestedAt = DateTimeOffset.UtcNow.AddSeconds(-_settings.MaxQueueSeconds - 5) };
        var recentPlayerOne = BuildPlayer(1010);
        var recentPlayerTwo = BuildPlayer(1020);

        await matchmaker.EnqueueAsync(stalePlayer);
        await matchmaker.EnqueueAsync(recentPlayerOne);
        var result = await matchmaker.EnqueueAsync(recentPlayerTwo);

        Assert.NotNull(result);
        Assert.DoesNotContain(stalePlayer, result!.Players);
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
