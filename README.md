# player-queue-service

Event-driven matchmaking queue worker built with .NET 10 and RabbitMQ. The service runs as a generic host with a background consumer that processes player queue events using durable queues, manual acknowledgements, and configuration validation at startup.

## Running locally

1. Install the .NET 10 SDK (`dotnet --info`).
2. Start RabbitMQ (defaults in `appsettings.Development.json` use localhost and the guest account).
3. Restore and build: `dotnet build PlayerQueueService.sln`.
4. Run the worker: `dotnet run --project PlayerQueueService.Api`.

The worker declares the configured exchange/queue topology on startup and will log consumed player events as they are processed. There is no HTTP surface area.

## Configuration

RabbitMQ options live under the `RabbitMQ` section in `appsettings*.json`:

- `HostName`, `Port`, `UserName`, `Password`, `VirtualHost`
- `QueueName`, `ExchangeName`, `RoutingKey`
- `PrefetchCount`, `MaxRetryAttempts`, `RetryDelaySeconds`, `PublishConfirmTimeoutSeconds`

All required fields are validated on host startup, and the worker stops if message processing or channel stability is compromised to preserve delivery.
