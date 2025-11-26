using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Services;

public class PlayerQueueProcessor : IPlayerQueueProcessor
{
    private readonly ILogger<PlayerQueueProcessor> _logger;

    public PlayerQueueProcessor(ILogger<PlayerQueueProcessor> logger)
    {
        _logger = logger;
    }

    public Task ProcessAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Matched player {PlayerId} in region {Region} for mode {Mode} at {RequestedAt}",
            playerEvent.PlayerId,
            playerEvent.Region,
            playerEvent.GameMode,
            playerEvent.RequestedAt);

        return Task.CompletedTask;
    }
}
