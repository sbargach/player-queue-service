using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Configuration;

public static class RabbitMqTopology
{
    public static void EnsureQueue(IModel channel, RabbitMqOptions options)
    {
        channel.ExchangeDeclare(options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);
        channel.QueueBind(options.QueueName, options.ExchangeName, options.RoutingKey);
    }
}
