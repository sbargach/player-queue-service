using System.Diagnostics.Metrics;
using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Telemetry;

public sealed class MetricsProvider : IMetricsProvider
{
    private readonly Meter _meter;
    private readonly Counter<long> _publishAttempts;
    private readonly Counter<long> _publishFailures;
    private readonly Counter<long> _publishSuccesses;
    private readonly Counter<long> _processingFailures;
    private readonly Counter<long> _processingSuccesses;
    private readonly Counter<long> _processingRetries;
    private readonly Histogram<double> _publishDuration;
    private readonly Histogram<double> _processingDuration;
    private readonly UpDownCounter<long> _inFlightProcessing;
    private bool _disposed;

    public MetricsProvider()
    {
        _meter = new Meter("player_queue_service", "1.0.0");
        _publishAttempts = _meter.CreateCounter<long>("playerqueue.publish.attempts", description: "Messages attempted to publish.");
        _publishFailures = _meter.CreateCounter<long>("playerqueue.publish.failures", description: "Failed publish attempts.");
        _publishSuccesses = _meter.CreateCounter<long>("playerqueue.publish.successes", description: "Successfully published messages.");
        _processingFailures = _meter.CreateCounter<long>("playerqueue.consume.failures", description: "Failed message processing attempts.");
        _processingSuccesses = _meter.CreateCounter<long>("playerqueue.consume.successes", description: "Successfully processed messages.");
        _processingRetries = _meter.CreateCounter<long>("playerqueue.consume.retries", description: "Processing retries triggered by failures.");
        _publishDuration = _meter.CreateHistogram<double>("playerqueue.publish.duration.ms", unit: "ms", description: "Duration of publish operations.");
        _processingDuration = _meter.CreateHistogram<double>("playerqueue.consume.duration.ms", unit: "ms", description: "Duration of consume operations.");
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

    public void IncrementProcessingRetry(PlayerEnqueuedEvent playerEvent, string queueName) =>
        _processingRetries.Add(1, BuildTags(playerEvent, queueName));

    public void IncrementConsumeSuccess(PlayerEnqueuedEvent playerEvent, string queueName) =>
        _processingSuccesses.Add(1, BuildTags(playerEvent, queueName));

    public void IncrementConsumeFailure(PlayerEnqueuedEvent playerEvent, string reason, string queueName) =>
        _processingFailures.Add(1, BuildFailureTags(playerEvent, reason, queueName));

    public void RecordConsumeDuration(PlayerEnqueuedEvent playerEvent, double milliseconds, string queueName) =>
        _processingDuration.Record(milliseconds, BuildTags(playerEvent, queueName));

    public void IncrementInFlight(string queueName) =>
        _inFlightProcessing.Add(1, new KeyValuePair<string, object?>("queue", queueName));

    public void DecrementInFlight(string queueName) =>
        _inFlightProcessing.Add(-1, new KeyValuePair<string, object?>("queue", queueName));

    private static KeyValuePair<string, object?>[] BuildTags(PlayerEnqueuedEvent playerEvent, string queueName = "") =>
        new[]
        {
            new KeyValuePair<string, object?>("region", playerEvent.Region),
            new KeyValuePair<string, object?>("mode", playerEvent.GameMode),
            new KeyValuePair<string, object?>("queue", queueName)
        };

    private static KeyValuePair<string, object?>[] BuildFailureTags(PlayerEnqueuedEvent playerEvent, string reason, string queueName = "") =>
        new[]
        {
            new KeyValuePair<string, object?>("region", playerEvent.Region),
            new KeyValuePair<string, object?>("mode", playerEvent.GameMode),
            new KeyValuePair<string, object?>("queue", queueName),
            new KeyValuePair<string, object?>("reason", reason)
        };

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
