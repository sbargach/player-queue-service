namespace PlayerQueueService.Api.Models.Events;

public record PlayerEnqueuedEvent
{
    /// <summary>
    /// Stable identifier used for idempotent consumption.
    /// </summary>
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public Guid PlayerId { get; init; }
    public int SkillRating { get; init; }
    public string Region { get; init; } = string.Empty;
    public string GameMode { get; init; } = string.Empty;
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
