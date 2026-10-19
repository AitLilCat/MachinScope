using Core.Domain.Models;
namespace Core.Application.Services;
public sealed class ConnectionTracker
{
    private Dictionary<string, (string Id, DateTimeOffset First)> current = new();
    private Dictionary<string, ConnectionRow> observed = new();
    private readonly Queue<ConnectionEvent> events = new();
    private long sequence;
    public IReadOnlyList<ConnectionEvent> Events => events.ToArray();
    private void Event(DateTimeOffset time, ConnectionEventKind kind, ConnectionRow row, string? previous = null)
    {
        events.Enqueue(new(++sequence, time, kind, row, previous)); while (events.Count > 2048) events.Dequeue();
    }
    public IReadOnlyList<ConnectionRow> Update(IReadOnlyList<ConnectionRow> rows, DateTimeOffset time)
    {
        var next = new Dictionary<string, (string Id, DateTimeOffset First)>(); var result = new List<ConnectionRow>();
        foreach (var row in rows)
        {
            // A native bind timestamp can preserve socket observation even when the owner is unreadable.
            // Owner actions still require process creation time; unknown owners never become controllable.
            var key = row.EndpointKey; if (next.ContainsKey(key)) continue;
            var entry = (row.OwnerStarted != null || row.NativeCreated > 0) && current.TryGetValue(key, out var old) ? old : (Guid.NewGuid().ToString("N"), time);
            next[key] = entry; result.Add(row with { ObservationId = entry.Item1, FirstObserved = entry.Item2 });
        }
        var tracked = result.Where(r => r.OwnerStarted != null || r.NativeCreated > 0).ToDictionary(r => r.ObservationId!);
        foreach (var row in tracked.Values)
        {
            if (!observed.TryGetValue(row.ObservationId!, out var old)) Event(time, ConnectionEventKind.Appeared, row);
            else if (old.State != row.State) Event(time, ConnectionEventKind.StateChanged, row, old.State);
        }
        foreach (var row in observed.Values) if (!tracked.ContainsKey(row.ObservationId!)) Event(time, ConnectionEventKind.Disappeared, row);
        observed = tracked; current = next; return result;
    }
}
