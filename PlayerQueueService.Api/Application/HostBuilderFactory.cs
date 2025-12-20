using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Globalization;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.HealthChecks;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Services;
using PlayerQueueService.Api.Telemetry;
using Serilog;

namespace PlayerQueueService.Api.Application;

public static class HostBuilderFactory
{
    public static IHostBuilder Create(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
            })
            .ConfigureServices((context, services) =>
            {
                ConfigureRabbitMQ(context, services);
                ConfigureMatchmaking(context, services);
                ConfigureTelemetry(context, services);
                services.AddHostedService<PlayerQueueConsumer>();
            });

    private static void ConfigureRabbitMQ(HostBuilderContext context, IServiceCollection services)
    {
        var rabbitSection = context.Configuration.GetSection("RabbitMQ");
        if (!rabbitSection.Exists())
        {
            rabbitSection = context.Configuration.GetSection("RabbitMq");
        }

        services.AddOptions<RabbitMQSettings>()
            .Bind(rabbitSection)
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.QueueName), "QueueName is required")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ExchangeName), "ExchangeName is required")
            .Validate(options => !string.IsNullOrWhiteSpace(options.RoutingKey), "RoutingKey is required")
            .Validate(options => !string.IsNullOrWhiteSpace(options.HostName), "HostName is required")
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddSingleton<IPlayerQueuePublisher, PlayerQueuePublisher>();
        services.AddSingleton<IMatchResultPublisher, MatchResultPublisher>();
        services.AddSingleton<IPlayerQueueProcessor, PlayerQueueProcessor>();
        services.AddSingleton<IMatchmaker, Matchmaker>();
        services.AddSingleton<IMetricsProvider, MetricsProvider>();
        services.AddHealthChecks()
            .AddCheck<RabbitMqPublisherHealthCheck>("rabbitmq_publisher")
            .AddCheck<RabbitMqConsumerHealthCheck>("rabbitmq_consumer");
    }

    private static void ConfigureMatchmaking(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<MatchmakingSettings>()
            .Bind(context.Configuration.GetSection("Matchmaking"))
            .ValidateDataAnnotations()
            .Validate(options => options.TeamSize > 1, "TeamSize must be at least 2")
            .ValidateOnStart();
    }

    private static void ConfigureTelemetry(HostBuilderContext context, IServiceCollection services)
    {
        services.Configure<TelemetryOptions>(context.Configuration.GetSection("Telemetry"));
        var telemetryOptions = context.Configuration.GetSection("Telemetry").Get<TelemetryOptions>() ?? new TelemetryOptions();

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    telemetryOptions.ServiceName,
                    serviceVersion: telemetryOptions.ServiceVersion);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(telemetryOptions.MeterName)
                    .AddRuntimeInstrumentation();

                var metricsEndpoint = telemetryOptions.Metrics.Endpoint;
                var metricsProtocol = telemetryOptions.Metrics.Protocol;
                if (string.IsNullOrWhiteSpace(metricsEndpoint) || string.IsNullOrWhiteSpace(metricsProtocol))
                {
                    Log.Warning("Telemetry metrics endpoint not configured; OTLP metrics export disabled. No metrics will be available.");
                    return;
                }

                metrics.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri(metricsEndpoint);
                    exporter.Protocol = ParseProtocol(metricsProtocol);
                });
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Tracing.ActivitySourceName)
                    .SetSampler(new AlwaysOnSampler());

                var tracesEndpoint = telemetryOptions.Tracing.Endpoint;
                var tracesProtocol = telemetryOptions.Tracing.Protocol;
                if (string.IsNullOrWhiteSpace(tracesEndpoint))
                {
                    return;
                }

                tracing.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri(tracesEndpoint);
                    exporter.Protocol = ParseProtocol(tracesProtocol);
                });
            });
    }

    private static OtlpExportProtocol ParseProtocol(string? protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        if (protocol.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        return Enum.TryParse<OtlpExportProtocol>(protocol, ignoreCase: true, out var parsed)
            ? parsed
            : OtlpExportProtocol.HttpProtobuf;
    }
}
