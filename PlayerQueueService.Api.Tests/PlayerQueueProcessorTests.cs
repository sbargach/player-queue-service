using Microsoft.Extensions.Logging.Abstractions;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Services;

namespace PlayerQueueService.Api.Tests;

public class PlayerQueueProcessorTests
{
    [Fact]
    public async Task ProcessAsync_CompletesWhenNotCancelled()
    {
        var processor = new PlayerQueueProcessor(NullLogger<PlayerQueueProcessor>.Instance);
        var playerEvent = new PlayerEnqueuedEvent
        {
            PlayerId = Guid.NewGuid(),
            Region = "na",
            GameMode = "duos",
            RequestedAt = DateTimeOffset.UtcNow
        };

        await processor.ProcessAsync(playerEvent);
    }

    [Fact]
    public async Task ProcessAsync_ThrowsWhenCancellationRequested()
    {
        var processor = new PlayerQueueProcessor(NullLogger<PlayerQueueProcessor>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => processor.ProcessAsync(new PlayerEnqueuedEvent(), cts.Token));
    }
}
