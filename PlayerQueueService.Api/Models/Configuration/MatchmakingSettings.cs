using System.ComponentModel.DataAnnotations;

namespace PlayerQueueService.Api.Models.Configuration;

/// <summary>
/// Settings controlling matchmaking constraints.
/// </summary>
public sealed record MatchmakingSettings
{
    /// <summary>
    /// Number of players required to form a match.
    /// </summary>
    [Range(2, 10)]
    public int TeamSize { get; init; } = 2;

    /// <summary>
    /// Maximum allowed difference between the lowest and highest skill rating in a match.
    /// </summary>
    [Range(10, 2000)]
    public int MaxSkillDelta { get; init; } = 200;

    /// <summary>
    /// Maximum seconds a player can wait in queue before they are dropped.
    /// </summary>
    [Range(10, 600)]
    public int MaxQueueSeconds { get; init; } = 120;
}
