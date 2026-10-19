using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Application.Services;

// Controlled managed reconciliation benchmark. Does not measure XAML rendering or input latency.
var output = args.Length > 0 ? args[0] : "baseline.json";
if (args.Length > 1 && args[1] == "pipeline") { await Pipeline.Run(output); return; }
var optimized = args.Length > 1 && args[1] == "optimized";
var results = new List<object>();
foreach (var count in new[] { 1_000, 10_000, 50_000, 100_000 })
foreach (var scenario in new[] { "unchanged", "updates-1pct", "updates-10pct", "shuffle", "remove-half", "append-1pct" })
{
    var times = new List<double>(); var allocations = new List<long>(); var samples = new List<object>();
    for (var repeat = 0; repeat < 3; repeat++)
    {
        var source = Enumerable.Range(0, count).Select(i => new Row(i)).ToArray();
        var desired = source.ToList();
        var random = new Random(753);
        if (scenario == "shuffle") random.Shuffle(CollectionsMarshal.AsSpan(desired));
        if (scenario == "remove-half") desired.RemoveRange(count / 2, count - count / 2);
        if (scenario == "append-1pct") desired.AddRange(Enumerable.Range(count, count / 100).Select(i => new Row(i)));
        var rows = new ReconciledCollection<Row>(source);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var heapBefore = GC.GetTotalMemory(false);
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        if (scenario.StartsWith("updates")) for (var i = 0; i < count / (scenario == "updates-1pct" ? 100 : 10); i++) desired[i].Value++;
        if (optimized) rows.Reconcile(desired); else Legacy(rows, desired);
        watch.Stop(); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes); times.Add(watch.Elapsed.TotalMilliseconds);
        if (!rows.SequenceEqual(desired)) throw new Exception("Incorrect reconciliation");
        process.Refresh(); samples.Add(new { repeat, latencyMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocations[^1], allocationBytesPerSecond = allocations[^1] / Math.Max(watch.Elapsed.TotalSeconds, 0.000001), cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, workingSetBytes = process.WorkingSet64, heapBeforeBytes = heapBefore, heapAfterBytes = GC.GetTotalMemory(false), errorCount = 0 });
    }
    times.Sort(); allocations.Sort();
    results.Add(new { count, scenario, medianMs = times[1], minMs = times[0], maxMs = times[2], medianAllocatedBytes = allocations[1], samples });
    Console.WriteLine($"{count} {scenario}: {times[1]:F2} ms [{times[0]:F2}, {times[2]:F2}]");
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), processors = Environment.ProcessorCount, build = "Release", coreVersion = typeof(ReconciledCollection<>).Assembly.GetName().Version?.ToString(), algorithm = optimized ? "hybrid" : "legacy", seed = 753, repeats = 3, results }, new JsonSerializerOptions { WriteIndented = true }));
static void Legacy(ObservableCollection<Row> rows, IReadOnlyList<Row> desired)
{
    var wanted = desired.Select(r => r.Key).ToHashSet();
    for (var i = rows.Count - 1; i >= 0; i--) if (!wanted.Contains(rows[i].Key)) rows.RemoveAt(i);
    for (var i = 0; i < desired.Count; i++)
    {
        if (i < rows.Count && rows[i].Key == desired[i].Key) continue;
        var existing = rows.IndexOf(desired[i]);
        if (existing >= 0) rows.Move(existing, i); else rows.Insert(i, desired[i]);
    }
}
sealed class Row(int key) { public int Key { get; } = key; public int Value { get; set; } }
