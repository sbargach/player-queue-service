using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using PlayerQueueService.Api.Models.Configuration;
using PlayerQueueService.Api.Services;
using Shouldly;

namespace PlayerQueueService.Api.Tests;

public class InMemoryIdempotencyStoreTests
{
    [Test]
    public async Task ConcurrentDeliveriesOfSameMessageAreSerialized()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new InMemoryIdempotencyStore(Options.Create(new RabbitMQSettings()), cache);
        var active = 0;
        var overlaps = 0;

        var deliveries = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var lease = await store.AcquireAsync("message-1");
            lease.ShouldProcess.ShouldBeTrue();
            var current = Interlocked.Increment(ref active);
            if (current > 1)
            {
                Interlocked.Increment(ref overlaps);
            }
            await Task.Delay(5);
            Interlocked.Decrement(ref active);
        });

        await Task.WhenAll(deliveries);
        overlaps.ShouldBe(0);
    }

    [Test]
    public async Task ProcessedMessageIsSkippedAfterLeaseIsReleased()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new InMemoryIdempotencyStore(Options.Create(new RabbitMQSettings()), cache);

        await using (var lease = await store.AcquireAsync("message-1"))
        {
            lease.ShouldProcess.ShouldBeTrue();
            await lease.MarkProcessedAsync();
        }

        await using var duplicate = await store.AcquireAsync("message-1");
        duplicate.ShouldProcess.ShouldBeFalse();
    }
}
