using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Configuration;

public static class RabbitMqTopology
{
    public static void EnsureQueue(IModel channel, RabbitMQSettings settings)
    {
        channel.ExchangeDeclare(settings.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(
            queue: settings.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);
        channel.QueueBind(settings.QueueName, settings.ExchangeName, settings.RoutingKey);
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
