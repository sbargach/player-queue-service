using NSubstitute;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Tests;

public class RabbitMqTopologyTests
{
    [Fact]
    public void EnsureQueue_DeclaresDurableExchangeQueueAndBinding()
    {
        var model = Substitute.For<IModel>();
        var settings = new RabbitMQSettings
        {
            ExchangeName = "player-queue",
            QueueName = "player-queue.enqueued",
            RoutingKey = "player.queue.enqueued"
        };

        RabbitMqTopology.EnsureQueue(model, settings);

        model.Received(1).ExchangeDeclare(settings.ExchangeName, ExchangeType.Topic, true, false);
        model.Received(1).QueueDeclare(settings.QueueName, true, false, false);
        model.Received(1).QueueBind(settings.QueueName, settings.ExchangeName, settings.RoutingKey);
    }

    [Fact]
    public void EnsureMatchResultsQueue_DeclaresDurableExchangeQueueAndBinding()
    {
        var model = Substitute.For<IModel>();
        var settings = new RabbitMQSettings
        {
            MatchResultsExchangeName = "player-match",
            MatchResultsQueueName = "player-match.formed",
            MatchResultsRoutingKey = "player.match.formed"
        };

        RabbitMqTopology.EnsureMatchResultsQueue(model, settings);

        model.Received(1).ExchangeDeclare(settings.MatchResultsExchangeName, ExchangeType.Topic, true, false);
        model.Received(1).QueueDeclare(settings.MatchResultsQueueName, true, false, false);
        model.Received(1).QueueBind(settings.MatchResultsQueueName, settings.MatchResultsExchangeName, settings.MatchResultsRoutingKey);
    }
}
