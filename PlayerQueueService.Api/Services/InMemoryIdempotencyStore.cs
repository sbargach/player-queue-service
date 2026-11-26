using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.Services;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, MessageLock> _locks = new();
    private readonly object _locksGate = new();
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

        MessageLock messageLock;
        lock (_locksGate)
        {
            if (_cache.TryGetValue(messageId, out _))
            {
                return IdempotencyLease.AlreadyProcessed(messageId);
            }

            if (!_locks.TryGetValue(messageId, out messageLock!))
            {
                messageLock = new MessageLock();
                _locks.Add(messageId, messageLock);
            }

            messageLock.Users++;
        }

        try
        {
            await messageLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseReference(messageId, messageLock);
            throw;
        }

        if (_cache.TryGetValue(messageId, out _))
        {
            Release(messageId, messageLock);
            return IdempotencyLease.AlreadyProcessed(messageId);
        }

        return new IdempotencyLease(
            messageId,
            onProcessed: () =>
            {
                MarkProcessed(messageId);
                return ValueTask.CompletedTask;
            },
            onDispose: () =>
            {
                Release(messageId, messageLock);
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

    private void Release(string messageId, MessageLock messageLock)
    {
        messageLock.Semaphore.Release();
        ReleaseReference(messageId, messageLock);
    }

    private void ReleaseReference(string messageId, MessageLock messageLock)
    {
        lock (_locksGate)
        {
            if (--messageLock.Users == 0)
            {
                _locks.Remove(messageId);
            }
        }
    }

    private sealed class MessageLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }
}
