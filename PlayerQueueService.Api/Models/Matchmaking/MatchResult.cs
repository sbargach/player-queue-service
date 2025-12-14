using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Models.Matchmaking;

public sealed record MatchResult(
    Guid MatchId,
    IReadOnlyCollection<PlayerEnqueuedEvent> Players,
    string Region,
    string GameMode,
    DateTimeOffset MatchedAt,
    int AverageSkillRating);
