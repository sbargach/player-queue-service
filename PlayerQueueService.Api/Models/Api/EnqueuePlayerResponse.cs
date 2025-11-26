namespace PlayerQueueService.Api.Models.Api;

public record EnqueuePlayerResponse(Guid PlayerId, string Region, string GameMode, DateTimeOffset RequestedAt);
