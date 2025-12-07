using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Globalization;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Messaging.Connectivity;
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
                ConfigureTelemetry(services);
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
        services.AddSingleton<IPlayerQueueProcessor, PlayerQueueProcessor>();
        services.AddSingleton<IMatchmaker, Matchmaker>();
        services.AddSingleton<IMetricsProvider, MetricsProvider>();
    }

    private static void ConfigureMatchmaking(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<MatchmakingSettings>()
            .Bind(context.Configuration.GetSection("Matchmaking"))
            .ValidateDataAnnotations()
            .Validate(options => options.TeamSize > 1, "TeamSize must be at least 2")
            .ValidateOnStart();
    }

    private static void ConfigureTelemetry(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("player-queue-service.api"))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("PlayerQueueService.Telemetry")
                    .AddRuntimeInstrumentation()
                    .AddConsoleExporter();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Tracing.ActivitySourceName)
                    .SetSampler(new AlwaysOnSampler())
                    .AddConsoleExporter()
                    .AddOtlpExporter();
            });
    }
}
