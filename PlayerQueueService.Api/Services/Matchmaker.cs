using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;
using PlayerQueueService.Api.Telemetry;

namespace PlayerQueueService.Api.Services;

public sealed class Matchmaker : IMatchmaker
{
    private readonly MatchmakingSettings _settings;
    private readonly IMetricsProvider _metrics;
    private readonly ILogger<Matchmaker> _logger;
    private readonly ConcurrentDictionary<string, MatchBucket> _buckets = new();

    public Matchmaker(
        IOptions<MatchmakingSettings> settings,
        IMetricsProvider metrics,
        ILogger<Matchmaker> logger)
    {
        _settings = settings.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public ValueTask<MatchResult?> EnqueueAsync(PlayerEnqueuedEvent playerEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bucketKey = $"{playerEvent.Region}:{playerEvent.GameMode}".ToLowerInvariant();
        var bucket = _buckets.GetOrAdd(bucketKey, _ => new MatchBucket(_settings, _metrics, _logger));

        var match = bucket.Add(playerEvent);
        return ValueTask.FromResult(match);
    }

    private sealed class MatchBucket
    {
        private readonly List<PlayerEnqueuedEvent> _waiting = new();
        private readonly object _syncRoot = new();
        private readonly MatchmakingSettings _settings;
        private readonly IMetricsProvider _metrics;
        private readonly ILogger _logger;

        public MatchBucket(MatchmakingSettings settings, IMetricsProvider metrics, ILogger logger)
        {
            _settings = settings;
            _metrics = metrics;
            _logger = logger;
        }

        public MatchResult? Add(PlayerEnqueuedEvent playerEvent)
        {
            lock (_syncRoot)
            {
                PruneExpired();

                _waiting.Add(playerEvent);
                if (_waiting.Count < _settings.TeamSize)
                {
                    return null;
                }

                var match = TryBuildMatch();
                if (match is null)
                {
                    return null;
                }

                foreach (var matchedPlayer in match.Players)
                {
                    _waiting.Remove(matchedPlayer);
                }

                _metrics.IncrementMatchFormed(match);
                _metrics.RecordQueueWait(match);

                _logger.LogInformation(
                    "Formed match with {Count} players in region {Region} for mode {Mode} (avg skill {Average})",
                    match.Players.Count,
                    match.Region,
                    match.GameMode,
                    match.AverageSkillRating);

                return match;
            }
        }

        private MatchResult? TryBuildMatch()
        {
            var ordered = _waiting
                .OrderBy(player => player.SkillRating)
                .ToList();

            for (var index = 0; index + _settings.TeamSize <= ordered.Count; index++)
            {
                var window = ordered
                    .Skip(index)
                    .Take(_settings.TeamSize)
                    .ToList();

                var delta = window[^1].SkillRating - window[0].SkillRating;
                if (delta > _settings.MaxSkillDelta)
                {
                    continue;
                }

                var averageSkill = (int)Math.Round(window.Average(player => player.SkillRating));
                return new MatchResult(
                    window,
                    window[0].Region,
                    window[0].GameMode,
                    DateTimeOffset.UtcNow,
                    averageSkill);
            }

            return null;
        }

        private void PruneExpired()
        {
            var now = DateTimeOffset.UtcNow;
            _waiting.RemoveAll(player => (now - player.RequestedAt).TotalSeconds > _settings.MaxQueueSeconds);
        }
    }
}
