using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Models.Configuration;
using RabbitMQ.Client;

namespace PlayerQueueService.Api.Messaging.Connectivity;

public sealed class RabbitMqConnection : IRabbitMqConnection
{
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private readonly object _syncRoot = new();

    public RabbitMqConnection(IOptions<RabbitMQSettings> options)
    {
        var settings = options.Value ?? throw new ArgumentNullException(nameof(options));

        _factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            VirtualHost = settings.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(settings.HeartbeatSeconds)
        };

        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            _factory.UserName = settings.UserName;
        }

        if (!string.IsNullOrWhiteSpace(settings.Password))
        {
            _factory.Password = settings.Password;
        }
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureConnection();
                return _connection!;
            },
            cancellationToken);

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
        lock (_syncRoot)
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
                _connection = null;
            }
        }
    }
}
