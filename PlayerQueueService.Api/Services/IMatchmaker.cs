using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;

namespace PlayerQueueService.Api.Services;

public interface IMatchmaker
{
    ValueTask<MatchResult?> EnqueueAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default);

    void ConfirmPublished(MatchResult match);

    void RestoreAfterPublishFailure(MatchResult match, string currentMessageId);
}
