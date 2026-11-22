using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
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
    private readonly ConcurrentDictionary<(string Region, string GameMode), MatchBucket> _buckets = new();

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

        var bucket = GetBucket(playerEvent.Region, playerEvent.GameMode);

        var match = bucket.Add(playerEvent);
        return ValueTask.FromResult(match);
    }

    public void ConfirmPublished(MatchResult match)
    {
        _metrics.IncrementMatchFormed(match);
        _metrics.RecordQueueWait(match);

        _logger.LogInformation(
            "Formed match with {Count} players in region {Region} for mode {Mode} (avg skill {Average})",
            match.Players.Count,
            match.Region,
            match.GameMode,
            match.AverageSkillRating);
    }

    public void RestoreAfterPublishFailure(MatchResult match, string currentMessageId) =>
        GetBucket(match.Region, match.GameMode).Restore(match.Players, currentMessageId);

    private MatchBucket GetBucket(string region, string gameMode)
    {
        var key = (region.ToLowerInvariant(), gameMode.ToLowerInvariant());
        return _buckets.GetOrAdd(key, _ => new MatchBucket(_settings));
    }

    private sealed class MatchBucket
    {
        private readonly List<PlayerEnqueuedEvent> _waiting = new();
        private readonly object _syncRoot = new();
        private readonly MatchmakingSettings _settings;

        public MatchBucket(MatchmakingSettings settings)
        {
            _settings = settings;
        }

        public MatchResult? Add(PlayerEnqueuedEvent playerEvent)
        {
            lock (_syncRoot)
            {
                PruneExpired();

                if (_waiting.All(player => player.MessageId != playerEvent.MessageId))
                {
                    _waiting.Add(playerEvent);
                }
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

                return match;
            }
        }

        public void Restore(IReadOnlyCollection<PlayerEnqueuedEvent> players, string currentMessageId)
        {
            lock (_syncRoot)
            {
                foreach (var player in players)
                {
                    if (player.MessageId != currentMessageId &&
                        _waiting.All(waiting => waiting.MessageId != player.MessageId))
                    {
                        _waiting.Add(player);
                    }
                }
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
                var messageIds = window.Select(player => player.MessageId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
                var matchId = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(messageIds)).AsSpan(0, 16));
                return new MatchResult(
                    matchId,
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
