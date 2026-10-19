namespace Core.Application.Services;

public enum RefreshState { Idle, Queued, Collecting, Cancelled, Failed, Applied, Stale }

/// <summary>One active collection and one replaceable pending request. Request from the
/// owning synchronization context; delegates and notifications resume on that context.</summary>
public sealed class RefreshCoordinator(LocalDiagnostics? diagnostics = null, DiagnosticMetric metric = DiagnosticMetric.Refresh) : IAsyncDisposable
{
    private readonly object gate = new();
    private Func<CancellationToken, Task>? pending;
    private CancellationTokenSource? active;
    private TaskCompletionSource? completion;
    private bool stopped;
    public RefreshState State { get; private set; }
    public event Action<RefreshState>? StateChanged;
    public event Action<Exception>? Failed;
    public bool IsBusy { get { lock (gate) return completion != null; } }
    public Task Request(Func<CancellationToken, Task> collect, bool replaceActive = false)
    {
        lock (gate)
        {
            if (stopped) return Task.CompletedTask;
            pending = collect;
            if (replaceActive) active?.Cancel();
            SetState(RefreshState.Queued);
            if (completion != null) return completion.Task;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = completion.Task;
            _ = Pump();
            return result;
        }
    }
    private async Task Pump()
    {
        await Task.Yield();
        try
        {
        while (true)
        {
            Func<CancellationToken, Task> collect;
            CancellationTokenSource local;
            lock (gate)
            {
                if (pending == null || stopped) { var done = completion; completion = null; done!.TrySetResult(); return; }
                collect = pending; pending = null; active = local = new();
            }
            var started = diagnostics?.Enabled == true ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { SetState(RefreshState.Collecting); await collect(local.Token); SetState(local.IsCancellationRequested ? RefreshState.Stale : RefreshState.Applied); }
            catch (OperationCanceledException) when (local.IsCancellationRequested) { SetState(RefreshState.Cancelled); }
            catch (Exception error) { SetState(RefreshState.Failed); Failed?.Invoke(error); }
            finally { if (started != 0) diagnostics!.Record(metric, System.Diagnostics.Stopwatch.GetElapsedTime(started), State); lock (gate) { active = null; local.Dispose(); } }
        }
        }
        catch (Exception observerError)
        {
            // An observer must not strand all callers or prevent later collection.
            lock (gate) { pending = null; var failed = completion; completion = null; failed?.TrySetException(observerError); }
        }
    }
    public Task CancelAndDrain()
    {
        lock (gate) { pending = null; active?.Cancel(); return completion?.Task ?? Task.CompletedTask; }
    }
    private void SetState(RefreshState state) { State = state; StateChanged?.Invoke(state); }
    public async ValueTask DisposeAsync()
    {
        Task drain;
        lock (gate) { stopped = true; pending = null; active?.Cancel(); drain = completion?.Task ?? Task.CompletedTask; }
        await drain;
        StateChanged = null; Failed = null;
    }
}

/// <summary>Page names alone cannot detect A→B→A navigation.</summary>
public sealed class NavigationRevision
{
    private long value;
    public long Current => Interlocked.Read(ref value);
    public long Advance() => Interlocked.Increment(ref value);
    public bool IsCurrent(long captured) => Current == captured;
}
