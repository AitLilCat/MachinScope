using Core.Application.Interfaces;
using Core.Application.Services;
namespace Core.Windows.Hardware;

// Scope is restricted to a read-only inventory operation. Fresh DetailsAsync/handle validation never enters it.
internal sealed class StorageQuerySession : IDisposable
{
    private static readonly AsyncLocal<StorageQuerySession?> slot = new();
    private readonly StorageQuerySession? previous = slot.Value;
    internal static StorageQuerySession? Current => slot.Value;
    internal int CacheHits;
    internal readonly Dictionary<string, List<Dictionary<string, string>>> Queries = new();
    internal StorageQuerySession() => slot.Value = this;
    public void Dispose() => slot.Value = previous;
}

public sealed class StorageSnapshots(ISystemInspector inspector, IDiskInspector disks) : IStorageSnapshots, IDisposable
{
    private readonly AsyncSnapshotCache<StorageSnapshot> cache = new(TimeSpan.FromSeconds(30));
    public int LastSharedQueryHits { get; private set; }
    public int LastQueryCount { get; private set; }
    public StorageSnapshot? Last => cache.Last;
    public Task<StorageSnapshot> ReadAsync(bool force, CancellationToken token) => cache.ReadAsync(async ct =>
    {
        using var scope = new StorageQuerySession();
        var table = PresentationTables.Storage(await inspector.StorageAsync(ct));
        var inventory = await disks.InventoryAsync(ct); ct.ThrowIfCancellationRequested();
        var rows = table.Data!.Select(row => row with { Subject = row.Raw[0] switch {
            "Physical disk" => (object?)inventory.Disks.FirstOrDefault(d => d.Target.Number.ToString() == row.Raw[1] && d.Target.Serial == row.Raw[3].Trim() && d.Target.Capacity.ToString() == row.Raw[5])?.Target,
            "Partition" => inventory.Disks.SelectMany(d => d.Partitions).FirstOrDefault(p => p.DeviceId == row.Raw[2]),
            "Volume" => inventory.Volumes.FirstOrDefault(v => v.Path.TrimEnd('\\') == row.Raw[1]), _ => null } }).ToArray();
        LastSharedQueryHits = scope.CacheHits; LastQueryCount = scope.Queries.Count;
        return new(inventory, table with { Data = rows }, DateTimeOffset.Now);
    }, force, token);
    public void Dispose() => cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
