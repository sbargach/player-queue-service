using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Services;

namespace PlayerQueueService.Api.Application;

public static class HostBuilderFactory
{
    public static IHostBuilder Create(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                ConfigureRabbitMQ(context, services);
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
    }
}
