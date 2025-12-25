using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.Services;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly TimeSpan _retentionWindow;

    public InMemoryIdempotencyStore(IOptions<RabbitMQSettings> options, IMemoryCache cache)
    {
        _cache = cache;
        _retentionWindow = TimeSpan.FromMinutes(Math.Max(1, options.Value.IdempotencyRetentionMinutes));
    }

    public async ValueTask<IdempotencyLease> AcquireAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("messageId cannot be null or whitespace.", nameof(messageId));
        }

        if (_cache.TryGetValue(messageId, out _))
        {
            _locks.TryRemove(messageId, out _);
            return IdempotencyLease.AlreadyProcessed(messageId);
        }

        var messageLock = _locks.GetOrAdd(messageId, _ => new SemaphoreSlim(1, 1));
        await messageLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (_cache.TryGetValue(messageId, out _))
        {
            messageLock.Release();
            _locks.TryRemove(messageId, out _);
            return IdempotencyLease.AlreadyProcessed(messageId);
        }

        var processed = false;

        return new IdempotencyLease(
            messageId,
            onProcessed: () =>
            {
                processed = true;
                MarkProcessed(messageId);
                return ValueTask.CompletedTask;
            },
            onDispose: () =>
            {
                Release(messageId, messageLock, processed);
                return ValueTask.CompletedTask;
            });
    }

    private void MarkProcessed(string messageId)
    {
        _cache.Set(
            messageId,
            true,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _retentionWindow
            });
    }

    private void Release(string messageId, SemaphoreSlim messageLock, bool markedProcessed)
    {
        messageLock.Release();

        if (markedProcessed || !_cache.TryGetValue(messageId, out _))
        {
            _locks.TryRemove(messageId, out _);
        }
    }
}
