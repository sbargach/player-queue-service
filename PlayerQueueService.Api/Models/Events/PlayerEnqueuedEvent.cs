using System.ComponentModel.DataAnnotations;

namespace PlayerQueueService.Api.Models.Events;

public record PlayerEnqueuedEvent
{
    /// <summary>
    /// Stable identifier used for idempotent consumption.
    /// </summary>
    [Required]
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Unique player identifier. Must be non-empty.
    /// </summary>
    public Guid PlayerId { get; init; }

    /// <summary>
    /// Player skill rating (MMR, rank points, etc).
    /// </summary>
    [Range(0, 5000)]
    public int SkillRating { get; init; }

    /// <summary>
    /// Region code for matchmaking.
    /// </summary>
    [Required, MinLength(1)]
    public string Region { get; init; } = string.Empty;

    /// <summary>
    /// Game mode being queued.
    /// </summary>
    [Required, MinLength(1)]
    public string GameMode { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when the player was queued.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
