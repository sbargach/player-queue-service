using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Connectivity;

public interface IRabbitMqConnection : IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    IModel CreateChannel();
}
