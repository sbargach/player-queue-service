using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.Messaging.HealthChecks;

public sealed class RabbitMqPublisherHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMQSettings _settings;
    private readonly ILogger<RabbitMqPublisherHealthCheck> _logger;

    public RabbitMqPublisherHealthCheck(
        IRabbitMqConnection connection,
        IOptions<RabbitMQSettings> options,
        ILogger<RabbitMqPublisherHealthCheck> logger)
    {
        _connection = connection;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            using var channel = _connection.CreateChannel();

            channel.ExchangeDeclarePassive(_settings.ExchangeName);
            channel.QueueDeclarePassive(_settings.QueueName);
            channel.QueueBind(_settings.QueueName, _settings.ExchangeName, _settings.RoutingKey, arguments: null);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ publisher health check canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ publisher health check failed.");
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
