using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Application.Services;
using Core.Domain.Models;

internal static class Pipeline
{
    public static async Task Run(string output)
    {
        var cases = new List<object>();
        void Measure(string name, Action action)
        {
            action(); var samples = new List<double>(); var allocated = new List<long>();
            for (var i = 0; i < 5; i++) { var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew(); action(); clock.Stop(); samples.Add(clock.Elapsed.TotalMilliseconds); allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before); }
            cases.Add(new { name, medianMs = samples.Order().ElementAt(2), samplesMs = samples, medianAllocatedBytes = allocated.Order().ElementAt(2) });
        }
        foreach (var count in new[] { 10_000, 100_000 })
        {
            var rows = Enumerable.Range(0, count).Select(i => { string[] cells = ["process-" + i, i.ToString()]; return new InspectionRow(i.ToString(), cells, cells, new Dictionary<string, object?> { ["name"] = cells[0], ["pid"] = i, ["memory"] = (long)i * 1000 }); }).ToArray();
            Measure($"filter-{count}", () => { var result = TableRows.FilterSort(rows, "memory>50MB", [], default); if (count == 100_000 && result.Count != 49_999) throw new Exception("Filter correctness failed"); });
            Measure($"sort-{count}", () => { var result = TableRows.FilterSort(rows, "", [new(1, true)], default); if (result[0].Key != (count - 1).ToString()) throw new Exception("Sort correctness failed"); });
        }
        Measure("navigate-10000", () => {
            var providers = new[] { new Capability("process.inspect", true, "", ""), new Capability("files.inspect", true, "", ""), new Capability("network.inspect", true, "", "") };
            var navigation = new NavigationCoordinator(PlatformKind.Windows, providers);
            for (var i = 0; i < 10000; i++) navigation.Select(i % 3 == 0 ? NavigationDestination.Processes : i % 3 == 1 ? NavigationDestination.Files : NavigationDestination.Connections);
        });
        var refreshSamples = new List<double>();
        for (var repeat = 0; repeat < 5; repeat++)
        {
            await using var coordinator = new RefreshCoordinator(); var calls = 0; var clock = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++) await coordinator.Request(_ => { calls++; return Task.CompletedTask; });
            clock.Stop(); if (calls != 1000) throw new Exception("Refresh correctness failed"); refreshSamples.Add(clock.Elapsed.TotalMilliseconds);
        }
        cases.Add(new { name = "refresh-1000", medianMs = refreshSamples.Order().ElementAt(2), samplesMs = refreshSamples });
        using var process = Process.GetCurrentProcess(); process.Refresh();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount, repeats = 5, workingSetBytes = process.WorkingSet64, processCpuMs = process.TotalProcessorTime.TotalMilliseconds, scope = "Managed orchestration/filter/sort only; excludes UI, native collectors, startup and sustained idle load", cases }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
