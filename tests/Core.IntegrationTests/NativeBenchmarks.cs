using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Windows.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
namespace Core.IntegrationTests;

internal static class NativeBenchmarks
{
    public static async Task Run(string output)
    {
        var inspector = new SystemInspector(NullLogger<SystemInspector>.Instance); var disks = new DiskInspector();
        using var canonical = new StorageSnapshots(inspector, disks); var samples = new List<object>();
        // Interleave old two-provider orchestration and new shared-query orchestration. Native providers are read-only.
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var watch = Stopwatch.StartNew(); var legacyTable = await inspector.StorageAsync(default); var legacyInventory = await disks.InventoryAsync(default); watch.Stop(); var legacyMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); var snapshot = await canonical.ReadAsync(true, default); watch.Stop(); var canonicalMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); var cached = await canonical.ReadAsync(false, default); watch.Stop();
            if (!ReferenceEquals(snapshot, cached)) throw new Exception("Warm snapshot was recollected");
            samples.Add(new { repeat, legacyMs, canonicalMs, cachedMs = watch.Elapsed.TotalMilliseconds, sharedQueryHits = canonical.LastSharedQueryHits, queryCount = canonical.LastQueryCount, legacyRows = legacyTable.Rows.Count, canonicalRows = snapshot.Table.Rows.Count, diskCount = snapshot.Inventory.Disks.Count, providerIssues = snapshot.Inventory.Issues.Count, inventoryChanged = legacyInventory.Disks.Count != snapshot.Inventory.Disks.Count });
        }
        using var process = Process.GetCurrentProcess(); process.Refresh();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount, repeats = 3, workingSetBytes = process.WorkingSet64, processCpuMs = process.TotalProcessorTime.TotalMilliseconds, scope = "Read-only real Windows storage; old two-provider sequence compared with shared-query snapshot and warm cache. Excludes XAML, startup and sustained application CPU.", samples }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
