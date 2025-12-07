using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Telemetry;

public interface IMetricsProvider : IDisposable
{
    void IncrementPublishAttempt(PlayerEnqueuedEvent playerEvent);
    void IncrementPublishSuccess(PlayerEnqueuedEvent playerEvent);
    void IncrementPublishFailure(PlayerEnqueuedEvent playerEvent, string reason);
    void RecordPublishDuration(PlayerEnqueuedEvent playerEvent, double milliseconds);
    void IncrementProcessingRetry(PlayerEnqueuedEvent playerEvent, string queueName);
    void IncrementConsumeSuccess(PlayerEnqueuedEvent playerEvent, string queueName);
    void IncrementConsumeFailure(PlayerEnqueuedEvent playerEvent, string reason, string queueName);
    void RecordConsumeDuration(PlayerEnqueuedEvent playerEvent, double milliseconds, string queueName);
    void IncrementInFlight(string queueName);
    void DecrementInFlight(string queueName);
    void IncrementMatchFormed(Models.Matchmaking.MatchResult match);
    void RecordQueueWait(Models.Matchmaking.MatchResult match);
}
