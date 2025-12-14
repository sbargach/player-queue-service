using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Services;

public class PlayerQueueProcessor : IPlayerQueueProcessor
{
    private readonly IMatchmaker _matchmaker;
    private readonly IMatchResultPublisher _matchResultPublisher;
    private readonly ILogger<PlayerQueueProcessor> _logger;

    public PlayerQueueProcessor(
        IMatchmaker matchmaker,
        IMatchResultPublisher matchResultPublisher,
        ILogger<PlayerQueueProcessor> logger)
    {
        _matchmaker = matchmaker;
        _matchResultPublisher = matchResultPublisher;
        _logger = logger;
    }

    public async Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var match = await _matchmaker.EnqueueAsync(playerEvent, cancellationToken).ConfigureAwait(false);
        if (match is not null)
        {
            await _matchResultPublisher.PublishAsync(match, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Matched {Count} players into {Mode} ({Region}) at avg skill {AverageSkill} (MatchId {MatchId})",
                match.Players.Count,
                match.GameMode,
                match.Region,
                match.AverageSkillRating,
                match.MatchId);
            return;
        }

        _logger.LogInformation(
            "Queued player {PlayerId} in region {Region} for mode {Mode} at {RequestedAt}",
            playerEvent.PlayerId,
            playerEvent.Region,
            playerEvent.GameMode,
            playerEvent.RequestedAt);
    }
}
