# player-queue-service

Event-driven matchmaking queue service built with .NET 10 and RabbitMQ. It publishes queue events for multiplayer games and runs a lightweight consumer to process them, keeping the HTTP layer thin and the messaging layer durable.

## Getting started

1. Install the .NET 10 SDK and run `dotnet --info` to confirm the version.
2. Start a local RabbitMQ instance (default credentials are used by `appsettings.Development.json`).
3. Restore and build: `dotnet build PlayerQueueService.sln`.
4. Run the API: `dotnet run --project PlayerQueueService.Api`.

POST `api/queue/enqueue` with a player payload to publish a `player.queue.enqueued` event. The hosted consumer logs processing and acknowledges messages using RabbitMQ best practices (durable queues, manual acks, and connection recovery).

Health endpoints:
- Liveness: `GET /health/live`
- Readiness: `GET /health/ready`
