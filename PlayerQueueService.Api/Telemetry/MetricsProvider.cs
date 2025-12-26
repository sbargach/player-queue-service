using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Models.Events;
using PlayerQueueService.Api.Models.Matchmaking;

namespace PlayerQueueService.Api.Telemetry;

public sealed class MetricsProvider : IMetricsProvider
{
    private readonly Meter _meter;
    private readonly Counter<long> _publishAttempts;
    private readonly Counter<long> _publishFailures;
    private readonly Counter<long> _publishSuccesses;
    private readonly Counter<long> _publishRetries;
    private readonly Counter<long> _processingFailures;
    private readonly Counter<long> _processingSuccesses;
    private readonly Counter<long> _processingRetries;
    private readonly Counter<long> _processedEntries;
    private readonly Counter<long> _matchesFormed;
    private readonly Counter<long> _validationFailures;
    private readonly Counter<long> _deadLetters;
    private readonly Histogram<double> _publishDuration;
    private readonly Histogram<double> _processingDuration;
    private readonly Histogram<double> _queueWait;
    private readonly UpDownCounter<long> _inFlightProcessing;
    private bool _disposed;

    public MetricsProvider(IOptions<TelemetryOptions> options)
    {
        var telemetry = options.Value;
        var meterVersion = telemetry.ServiceVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString();

        _meter = new Meter(telemetry.MeterName, meterVersion);
        _publishAttempts = _meter.CreateCounter<long>("playerqueue.publish.attempts", description: "Messages attempted to publish.");
        _publishFailures = _meter.CreateCounter<long>("playerqueue.publish.failures", description: "Failed publish attempts.");
        _publishSuccesses = _meter.CreateCounter<long>("playerqueue.publish.successes", description: "Successfully published messages.");
        _publishRetries = _meter.CreateCounter<long>("playerqueue.publish.retries", description: "Publish retries triggered by failures.");
        _processingFailures = _meter.CreateCounter<long>("playerqueue.consume.failures", description: "Failed message processing attempts.");
        _processingSuccesses = _meter.CreateCounter<long>("playerqueue.consume.successes", description: "Successfully processed messages.");
        _processingRetries = _meter.CreateCounter<long>("playerqueue.consume.retries", description: "Processing retries triggered by failures.");
        _processedEntries = _meter.CreateCounter<long>("playerqueue.processed.entries", description: "Entries processed from the queue.");
        _matchesFormed = _meter.CreateCounter<long>("playerqueue.match.formed", description: "Matches successfully formed.");
        _validationFailures = _meter.CreateCounter<long>("playerqueue.validation.failures", description: "Contract validation failures.");
        _deadLetters = _meter.CreateCounter<long>("playerqueue.dlq.published", description: "Messages sent to the dead-letter queue.");
        _publishDuration = _meter.CreateHistogram<double>("playerqueue.publish.duration.ms", unit: "ms", description: "Duration of publish operations.");
        _processingDuration = _meter.CreateHistogram<double>("playerqueue.consume.duration.ms", unit: "ms", description: "Duration of consume operations.");
        _queueWait = _meter.CreateHistogram<double>("playerqueue.queue.wait.ms", unit: "ms", description: "Time players waited before matchmaking.");
        _inFlightProcessing = _meter.CreateUpDownCounter<long>("playerqueue.consume.inflight", description: "Messages currently being processed.");
    }

    public void IncrementPublishAttempt(PlayerEnqueuedEvent playerEvent) =>
        _publishAttempts.Add(1, BuildTags(playerEvent));

    public void IncrementPublishSuccess(PlayerEnqueuedEvent playerEvent) =>
        _publishSuccesses.Add(1, BuildTags(playerEvent));

    public void IncrementPublishFailure(PlayerEnqueuedEvent playerEvent, string reason) =>
        _publishFailures.Add(1, BuildFailureTags(playerEvent, reason));

    public void RecordPublishDuration(PlayerEnqueuedEvent playerEvent, double milliseconds) =>
        _publishDuration.Record(milliseconds, BuildTags(playerEvent));

    public void IncrementPublishRetry(PlayerEnqueuedEvent playerEvent, string queueName) =>
        _publishRetries.Add(1, BuildTags(playerEvent, queueName));

    public void IncrementProcessingRetry(PlayerEnqueuedEvent playerEvent, string queueName) =>
        _processingRetries.Add(1, BuildTags(playerEvent, queueName));

    public void IncrementProcessed(PlayerEnqueuedEvent playerEvent, string queueName) =>
        _processedEntries.Add(1, BuildTags(playerEvent, queueName));

    public void IncrementConsumeSuccess(PlayerEnqueuedEvent playerEvent, string queueName) =>
        _processingSuccesses.Add(1, BuildTags(playerEvent, queueName));

    public void IncrementConsumeFailure(PlayerEnqueuedEvent playerEvent, string reason, string queueName) =>
        _processingFailures.Add(1, BuildFailureTags(playerEvent, reason, queueName));

    public void RecordConsumeDuration(PlayerEnqueuedEvent playerEvent, double milliseconds, string queueName) =>
        _processingDuration.Record(milliseconds, BuildTags(playerEvent, queueName));

    public void IncrementValidationFailure(PlayerEnqueuedEvent playerEvent, string scope, string reason, string queueName = "") =>
        _validationFailures.Add(1, BuildValidationTags(playerEvent, scope, reason, queueName));

    public void IncrementDeadLetter(PlayerEnqueuedEvent playerEvent, string reason, string queueName) =>
        _deadLetters.Add(1, BuildFailureTags(playerEvent, reason, queueName));

    public void IncrementInFlight(string queueName) =>
        _inFlightProcessing.Add(1, new KeyValuePair<string, object?>("queue", queueName));

    public void DecrementInFlight(string queueName) =>
        _inFlightProcessing.Add(-1, new KeyValuePair<string, object?>("queue", queueName));

    public void IncrementMatchFormed(MatchResult match) =>
        _matchesFormed.Add(1, BuildTags(match.Region, match.GameMode));

    public void RecordQueueWait(MatchResult match)
    {
        foreach (var player in match.Players)
        {
            var waitMs = (match.MatchedAt - player.RequestedAt).TotalMilliseconds;
            _queueWait.Record(waitMs, BuildTags(player.Region, player.GameMode));
        }
    }

    private static KeyValuePair<string, object?>[] BuildTags(PlayerEnqueuedEvent playerEvent, string queueName = "") =>
        BuildTags(playerEvent.Region, playerEvent.GameMode, queueName);

    private static KeyValuePair<string, object?>[] BuildTags(string region, string mode, string queueName = "") =>
        new[]
        {
            new KeyValuePair<string, object?>("region", region),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("queue", queueName)
        };

    private static KeyValuePair<string, object?>[] BuildFailureTags(PlayerEnqueuedEvent playerEvent, string reason, string queueName = "") =>
        BuildTags(playerEvent, queueName)
            .Append(new KeyValuePair<string, object?>("reason", reason))
            .ToArray();

    private static KeyValuePair<string, object?>[] BuildValidationTags(PlayerEnqueuedEvent playerEvent, string scope, string reason, string queueName = "") =>
        BuildTags(playerEvent, queueName)
            .Append(new KeyValuePair<string, object?>("scope", scope))
            .Append(new KeyValuePair<string, object?>("reason", reason))
            .ToArray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _meter.Dispose();
        _disposed = true;
    }
}
