using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Connectivity;

public sealed class RabbitMqConnection : IRabbitMqConnection
{
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private readonly object _syncRoot = new();

    public RabbitMqConnection(IOptions<RabbitMqOptions> options)
    {
        var settings = options.Value ?? throw new ArgumentNullException(nameof(options));

        _factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(settings.HeartbeatSeconds)
        };
    }

    public IModel CreateChannel()
    {
        EnsureConnection();
        return _connection!.CreateModel();
    }

    private void EnsureConnection()
    {
        if (_connection is { IsOpen: true })
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_connection is { IsOpen: true })
            {
                return;
            }

            _connection?.Dispose();
            _connection = _factory.CreateConnection("player-queue-service");
        }
    }

    public void Dispose()
    {
        if (_connection == null)
        {
            return;
        }

        try
        {
            if (_connection.IsOpen)
            {
                _connection.Close();
            }
        }
        finally
        {
            _connection.Dispose();
        }
    }
}
