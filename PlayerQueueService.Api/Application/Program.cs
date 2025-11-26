using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PlayerQueueService.Api.HealthChecks;
using PlayerQueueService.Api.Messaging.Consumers;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Messaging.Publishing;
using PlayerQueueService.Api.Models.Api;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Services;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder);

        var app = BuildApp(builder);

        ConfigureMiddleware(app);

        app.Run();
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value!.Errors.Count > 0)
                        .Select(e => new FieldError(
                            e.Key,
                            e.Value!.Errors.Select(err => err.ErrorMessage)));

                    return new BadRequestObjectResult(
                        ApiErrorResponse.ValidationFailed(errors));
                };
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddOptions<RabbitMqOptions>()
            .Bind(builder.Configuration.GetSection("RabbitMq"))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.QueueName), "QueueName is required")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ExchangeName), "ExchangeName is required")
            .ValidateOnStart();

        builder.Services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        builder.Services.AddSingleton<IPlayerQueuePublisher, PlayerQueuePublisher>();
        builder.Services.AddSingleton<IPlayerQueueProcessor, PlayerQueueProcessor>();
        builder.Services.AddHostedService<PlayerQueueConsumer>();

        builder.Services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Unhealthy);
    }

    private static WebApplication BuildApp(WebApplicationBuilder builder) => builder.Build();

    private static void ConfigureMiddleware(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.MapControllers();
        app.MapHealthChecks("/health");
    }
}
