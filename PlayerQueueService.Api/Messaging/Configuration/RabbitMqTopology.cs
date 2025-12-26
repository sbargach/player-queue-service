using System.Collections.Generic;
using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Configuration;

public static class RabbitMqTopology
{
    public static void EnsureQueue(IModel channel, RabbitMQSettings settings)
    {
        EnsureDeadLetterQueue(channel, settings);
        channel.ExchangeDeclare(settings.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(
            queue: settings.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = settings.DeadLetterExchangeName,
                ["x-dead-letter-routing-key"] = settings.DeadLetterRoutingKey
            });
        channel.QueueBind(settings.QueueName, settings.ExchangeName, settings.RoutingKey);
    }

    public static void EnsureDeadLetterQueue(IModel channel, RabbitMQSettings settings)
    {
        channel.ExchangeDeclare(settings.DeadLetterExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(
            queue: settings.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);
        channel.QueueBind(settings.DeadLetterQueueName, settings.DeadLetterExchangeName, settings.DeadLetterRoutingKey);
    }

    public static void EnsureMatchResultsQueue(IModel channel, RabbitMQSettings settings)
    {
        channel.ExchangeDeclare(settings.MatchResultsExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(
            queue: settings.MatchResultsQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);
        channel.QueueBind(settings.MatchResultsQueueName, settings.MatchResultsExchangeName, settings.MatchResultsRoutingKey);
    }
}
