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
}
