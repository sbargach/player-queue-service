using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.HealthChecks;

public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMQSettings _settings;

    public RabbitMqHealthCheck(IRabbitMqConnection connection, IOptions<RabbitMQSettings> settings)
    {
        _connection = connection;
        _settings = settings.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var channel = _connection.CreateChannel();
            RabbitMqTopology.EnsureQueue(channel, _settings);
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connection established."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection failed.", ex));
        }
    }
}
