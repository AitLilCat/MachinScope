namespace Core.Application.Services;

// Collection is serialized. A cancelled/failed result is never published; disposal drains the owner.
public sealed class AsyncSnapshotCache<T>(TimeSpan lifetime, TimeProvider? clock = null) : IAsyncDisposable where T : class
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private T? value;
    private long captured;
    private bool disposed;
    public T? Last => Volatile.Read(ref value);
    public async Task<T> ReadAsync(Func<CancellationToken, Task<T>> collect, bool force, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!force && value != null && time.GetElapsedTime(captured) < lifetime) return value;
            var next = await collect(token); token.ThrowIfCancellationRequested();
            captured = time.GetTimestamp(); Volatile.Write(ref value, next); return next;
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync() { await gate.WaitAsync(); try { disposed = true; value = null; } finally { gate.Release(); } }
}
