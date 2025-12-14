using PlayerQueueService.Api.Models.Matchmaking;

namespace PlayerQueueService.Api.Messaging.Publishing;

public interface IMatchResultPublisher
{
    Task PublishAsync(MatchResult match, CancellationToken cancellationToken = default);
}
