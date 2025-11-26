using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Messaging.Publishing;

public interface IPlayerQueuePublisher
{
    Task PublishAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default);
}
