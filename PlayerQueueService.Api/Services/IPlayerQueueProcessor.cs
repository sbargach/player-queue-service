using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Services;

public interface IPlayerQueueProcessor
{
    Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default);
}
