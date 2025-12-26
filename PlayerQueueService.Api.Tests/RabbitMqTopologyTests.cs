using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using PlayerQueueService.Api.Messaging.Configuration;
using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Tests;

public class RabbitMqTopologyTests
{
    [Test]
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
        model.Received(1).ExchangeDeclare(settings.DeadLetterExchangeName, ExchangeType.Topic, true, false);
        model.Received(1).QueueDeclare(
            settings.DeadLetterQueueName,
            true,
            false,
            false);
        model.Received(1).QueueBind(settings.DeadLetterQueueName, settings.DeadLetterExchangeName, settings.DeadLetterRoutingKey);
        model.Received(1).QueueDeclare(
            settings.QueueName,
            true,
            false,
            false,
            Arg.Is<IDictionary<string, object>>(dict =>
                dict.ContainsKey("x-dead-letter-exchange") &&
                dict.ContainsKey("x-dead-letter-routing-key") &&
                string.Equals(dict["x-dead-letter-exchange"] == null ? null : dict["x-dead-letter-exchange"].ToString(), settings.DeadLetterExchangeName, StringComparison.Ordinal) &&
                string.Equals(dict["x-dead-letter-routing-key"] == null ? null : dict["x-dead-letter-routing-key"].ToString(), settings.DeadLetterRoutingKey, StringComparison.Ordinal)));
        model.Received(1).QueueBind(settings.QueueName, settings.ExchangeName, settings.RoutingKey);
    }

    [Test]
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
