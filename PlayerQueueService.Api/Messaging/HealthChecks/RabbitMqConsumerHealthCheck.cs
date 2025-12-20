using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.Messaging.HealthChecks;

public sealed class RabbitMqConsumerHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMQSettings _settings;
    private readonly ILogger<RabbitMqConsumerHealthCheck> _logger;

    public RabbitMqConsumerHealthCheck(
        IRabbitMqConnection connection,
        IOptions<RabbitMQSettings> options,
        ILogger<RabbitMqConsumerHealthCheck> logger)
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

            channel.QueueDeclarePassive(_settings.QueueName);
            channel.BasicQos(0, _settings.PrefetchCount, global: false);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ consumer health check canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ consumer health check failed.");
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
