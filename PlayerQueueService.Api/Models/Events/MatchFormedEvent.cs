namespace PlayerQueueService.Api.Models.Events;

public sealed record MatchFormedEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public Guid MatchId { get; init; }
    public string Region { get; init; } = string.Empty;
    public string GameMode { get; init; } = string.Empty;
    public int AverageSkillRating { get; init; }
    public DateTimeOffset MatchedAt { get; init; }
    public IReadOnlyCollection<PlayerMatchEntry> Players { get; init; } = Array.Empty<PlayerMatchEntry>();
}

public sealed record PlayerMatchEntry
{
    public Guid PlayerId { get; init; }
    public int SkillRating { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
}
