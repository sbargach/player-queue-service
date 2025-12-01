using System.Collections.Generic;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using PlayerQueueService.Api.HealthChecks;
using PlayerQueueService.Api.Messaging.Connectivity;
using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Tests;

public class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenQueueTopologySucceeds()
    {
        var connection = Substitute.For<IRabbitMqConnection>();
        var channel = Substitute.For<IModel>();
        connection.CreateChannel().Returns(channel);

        var settings = Options.Create(new RabbitMQSettings());
        var healthCheck = new RabbitMqHealthCheck(connection, settings);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        connection.Received(1).CreateChannel();
        channel.Received(1).ExchangeDeclare(settings.Value.ExchangeName, ExchangeType.Topic, true, false, Arg.Any<IDictionary<string, object>>());
        channel.Received(1).QueueDeclare(settings.Value.QueueName, true, false, false, Arg.Any<IDictionary<string, object>>());
        channel.Received(1).QueueBind(settings.Value.QueueName, settings.Value.ExchangeName, settings.Value.RoutingKey, Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenChannelCreationThrows()
    {
        var connection = Substitute.For<IRabbitMqConnection>();
        connection.CreateChannel().Returns(_ => throw new InvalidOperationException("boom"));

        var settings = Options.Create(new RabbitMQSettings());
        var healthCheck = new RabbitMqHealthCheck(connection, settings);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }
}
