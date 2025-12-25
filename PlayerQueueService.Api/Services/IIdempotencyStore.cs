using System.Threading.Tasks;
using System.Threading;

namespace PlayerQueueService.Api.Services;

public interface IIdempotencyStore
{
    ValueTask<IdempotencyLease> AcquireAsync(string messageId, CancellationToken cancellationToken = default);
}

public sealed class IdempotencyLease : IAsyncDisposable
{
    private readonly Func<ValueTask> _onProcessed;
    private readonly Func<ValueTask> _onDispose;

    internal IdempotencyLease(
        string messageId,
        Func<ValueTask> onProcessed,
        Func<ValueTask> onDispose)
    {
        MessageId = messageId;
        ShouldProcess = true;
        _onProcessed = onProcessed;
        _onDispose = onDispose;
    }

    private IdempotencyLease(string messageId)
    {
        MessageId = messageId;
        ShouldProcess = false;
        _onProcessed = static () => ValueTask.CompletedTask;
        _onDispose = static () => ValueTask.CompletedTask;
    }

    public string MessageId { get; }

    public bool ShouldProcess { get; }

    public ValueTask MarkProcessedAsync()
    {
        if (!ShouldProcess)
        {
            return ValueTask.CompletedTask;
        }

        return _onProcessed();
    }

    public ValueTask DisposeAsync()
    {
        return _onDispose();
    }

    internal static IdempotencyLease AlreadyProcessed(string messageId) => new(messageId);
}
