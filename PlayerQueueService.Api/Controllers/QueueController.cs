using Microsoft.AspNetCore.Mvc;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Api;
using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Controllers;

[ApiController]
[Route("api/queue")]
public class QueueController : ControllerBase
{
    private readonly IPlayerQueuePublisher _publisher;

    public QueueController(IPlayerQueuePublisher publisher)
    {
        _publisher = publisher;
    }

    [HttpPost("enqueue")]
    public async Task<ActionResult<EnqueuePlayerResponse>> EnqueueAsync(
        [FromBody] EnqueuePlayerRequest request,
        CancellationToken cancellationToken)
    {
        var message = new PlayerEnqueuedEvent
        {
            PlayerId = request.PlayerId,
            SkillRating = request.SkillRating,
            Region = request.Region?.Trim() ?? string.Empty,
            GameMode = request.GameMode?.Trim() ?? string.Empty,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _publisher.PublishAsync(message, cancellationToken);

        return Accepted(new EnqueuePlayerResponse(
            message.PlayerId,
            message.Region,
            message.GameMode,
            message.RequestedAt));
    }
}
