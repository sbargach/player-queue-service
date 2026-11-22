using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Services;

public class PlayerQueueProcessor : IPlayerQueueProcessor
{
    private readonly IMatchmaker _matchmaker;
    private readonly IMatchResultPublisher _matchResultPublisher;
    private readonly ILogger<PlayerQueueProcessor> _logger;
    private readonly SemaphoreSlim _processingGate = new(1, 1);

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
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var match = await _matchmaker.EnqueueAsync(playerEvent, cancellationToken).ConfigureAwait(false);
            if (match is null)
            {
                _logger.LogInformation(
                    "Queued player {PlayerId} in region {Region} for mode {Mode} at {RequestedAt}",
                    playerEvent.PlayerId,
                    playerEvent.Region,
                    playerEvent.GameMode,
                    playerEvent.RequestedAt);
                return;
            }

            try
            {
                await _matchResultPublisher.PublishAsync(match, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _matchmaker.RestoreAfterPublishFailure(match, playerEvent.MessageId);
                throw;
            }

            _matchmaker.ConfirmPublished(match);
            _logger.LogInformation(
                "Matched {Count} players into {Mode} ({Region}) at avg skill {AverageSkill} (MatchId {MatchId})",
                match.Players.Count,
                match.GameMode,
                match.Region,
                match.AverageSkillRating,
                match.MatchId);
        }
        finally
        {
            _processingGate.Release();
        }
    }
}
