# Player Queue Service

I built this to play around with RabbitMQ and a .NET worker. Send in player events, and when two players in the same region and game mode have close enough skill ratings, the worker publishes a match. There is no UI or gameplay here. I was mostly interested in what happens around acknowledgements, retries, publisher confirms, and dead-letter queues.

## Run it

You need Docker. From the repo root:

```sh
docker compose up --build
```

Open [RabbitMQ Management](http://localhost:15672) with `guest` / `guest`. Go to **Exchanges**, open `player-queue`, and publish these two messages with routing key `player.queue.enqueued`:

```json
{"messageId":"demo-player-1","playerId":"bf1a65ef-e0a2-4909-9c1d-8cb2da7f5e01","skillRating":1200,"region":"eu","gameMode":"duos"}
```

```json
{"messageId":"demo-player-2","playerId":"bf1a65ef-e0a2-4909-9c1d-8cb2da7f5e02","skillRating":1210,"region":"eu","gameMode":"duos"}
```

The worker should log a match. You can find the event in the `player-match.formed` queue. If you send the first message again, it should be skipped as a duplicate. The worker also has [liveness](http://localhost:8080/health/live) and [readiness](http://localhost:8080/health/ready) endpoints.

`requestedAt` is left out of the examples so it defaults to the current time. If you do set it yourself, keep it recent: the matcher drops players older than `MaxQueueSeconds` (120 seconds by default).

## A few things to know

The matcher and the list of processed message IDs are in memory. If the worker restarts while a player is waiting, that player is lost even though RabbitMQ has acknowledged the input message. Running two workers would also give each worker its own separate queue of waiting players. This is a single-worker experiment, not durable matchmaking.

A result can be published twice if the worker dies after publishing it but before acknowledging the input. The match ID stays the same for the same pair of message IDs, so a real consumer of match events would need to deduplicate it. I would add persistent waiting state and an outbox before using this design for anything that needs reliable delivery across restarts.

The consumer rejects invalid events to `player-queue.dead-letter`. That includes bodies that are not valid JSON and events without a `messageId`, because the id is what duplicate detection relies on. Processing failures are retried first; after the configured limit they go to the same dead-letter queue. Settings live in `PlayerQueueService.Api/appsettings.Development.json` and can be overridden with environment variables.

## Tests

```sh
dotnet test PlayerQueueService.Api.Tests/PlayerQueueService.Api.Tests.csproj
dotnet test PlayerQueueService.Api.IntegrationTests/PlayerQueueService.Api.IntegrationTests.csproj
```

The second test project uses a fake RabbitMQ connection. It checks the consumer flow but does not start a real broker. The Docker run above is the quick way to try the actual RabbitMQ path locally.
