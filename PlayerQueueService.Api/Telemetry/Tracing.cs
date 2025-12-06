using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace PlayerQueueService.Api.Telemetry;

public static class Tracing
{
    public const string ActivitySourceName = "PlayerQueueService.Messaging";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
}
