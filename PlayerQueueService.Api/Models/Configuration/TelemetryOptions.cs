namespace PlayerQueueService.Api.Models.Configuration;

public sealed record TelemetryOptions
{
    public string ServiceName { get; init; } = "player-queue-service.api";
    public string MeterName { get; init; } = "player-queue-service.metrics";
    public string? ServiceVersion { get; init; }
    public OtlpOptions Metrics { get; init; } = new();
    public OtlpOptions Tracing { get; init; } = new();

    public sealed record OtlpOptions
    {
        public string? Endpoint { get; init; }
        public string? Protocol { get; init; }
    }
}
