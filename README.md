# player-queue-service

Event-driven matchmaking queue worker built with .NET 10 and RabbitMQ. The service runs as a generic host with a background consumer that processes player queue events using durable queues, manual acknowledgements, configuration validation at startup, and minimal HTTP surface area (health probes only).

## Running locally

1. Install the .NET 10 SDK (`dotnet --info`).
2. Start RabbitMQ (defaults in `appsettings.Development.json` use localhost and the guest account).
3. Restore and build: `dotnet build PlayerQueueService.sln`.
4. Run the worker: `dotnet run --project PlayerQueueService.Api`.

The worker declares the configured exchange/queue topology on startup and will log consumed player events as they are processed. There is no HTTP surface area.

### Docker

You can run the worker and RabbitMQ locally without installing anything extra:

- Build and run: `docker compose up --build`
- RabbitMQ management UI: http://localhost:15672 (guest/guest)
- The worker listens for messages on `player-queue.enqueued` when the broker is healthy.
- Health probes: `/health/live` (always up) and `/health/ready` (wires publisher/consumer checks) on port 8080.

## Configuration

The base `appsettings.json` keeps only Serilog settings; local development defaults (RabbitMQ, telemetry, matchmaking) live in `appsettings.Development.json`. All options can still be overridden via environment variables.

RabbitMQ options live under the `RabbitMQ` section in `appsettings*.json`:

- `HostName`, `Port`, `UserName`, `Password`, `VirtualHost`
- `QueueName`, `ExchangeName`, `RoutingKey`
- `MatchResultsExchangeName`, `MatchResultsQueueName`, `MatchResultsRoutingKey`
- `PrefetchCount`, `MaxRetryAttempts`, `RetryDelaySeconds`, `PublishConfirmTimeoutSeconds`

All required fields are validated on host startup, and the worker stops if message processing or channel stability is compromised to preserve delivery.

### Matchmaking

The consumer uses a lightweight in-memory matcher to group players by region/mode. Defaults live under `Matchmaking` in `appsettings*.json`:

- `TeamSize`: number of players required to form a match (default 2).
- `MaxSkillDelta`: maximum allowed skill rating spread within a match.
- `MaxQueueSeconds`: players waiting longer than this are dropped before matching.

When a match forms, a telemetry event is emitted and queue wait durations are recorded per player.
