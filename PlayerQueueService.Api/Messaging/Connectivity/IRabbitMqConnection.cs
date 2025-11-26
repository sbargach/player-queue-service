using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Connectivity;

public interface IRabbitMqConnection : IDisposable
{
    IModel CreateChannel();
}
