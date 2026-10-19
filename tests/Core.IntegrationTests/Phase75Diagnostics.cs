using System.Diagnostics;
using System.Text.Json;
using Core.Application.Services;
using Core.Domain.Models;
using Core.Windows.Hardware;
namespace Core.IntegrationTests;

internal static class Phase75Diagnostics
{
    public static async Task Run(string destination)
    {
        var platform = new WindowsPlatform(); await using var tools = new WindowsAdministrationTools(platform, platform);
        var full = new List<double>(); var cached = new List<double>(); IReadOnlyList<ToolAvailability> states = [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 5; i++) {
            var watch = Stopwatch.StartNew(); states = await tools.InspectAsync(true, timeout.Token); full.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart(); await tools.InspectAsync(false, timeout.Token); cached.Add(watch.Elapsed.TotalMilliseconds);
        }
        var context = new InspectionContext(InspectionScope.Process, PlatformKind.Windows);
        for (var i = 0; i < 1000; i++) context.SelectId(i % 2 == 0 ? "process.threads" : "process.modules");
        var before = GC.GetAllocatedBytesForCurrentThread(); var selectionWatch = Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++) context.SelectId(i % 2 == 0 ? "process.threads" : "process.modules");
        selectionWatch.Stop(); var selectionAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        var searchWatch = Stopwatch.StartNew(); var matching = 0;
        for (var i = 0; i < 1000; i++) matching += AdministrationCatalog.Filter(PlatformKind.Windows, i % 2 == 0 ? "network" : "security").Count;
        searchWatch.Stop();
        await File.WriteAllTextAsync(Path.GetFullPath(destination), JsonSerializer.Serialize(new {
            schema = 1, measuredUtc = DateTimeOffset.UtcNow, host = platform.Platform,
            scope = "Non-interactive feature measurements. No MainWindow, UI rendering, native utility launches or Linux execution. No before/after UI performance claim.",
            tools = states.Count, availableOrNativeAuthorization = states.Count(s => s.CanOpen),
            forcedCatalogMilliseconds = full, cachedCatalogMilliseconds = cached,
            metadataSelections = 100_000, metadataSelectionMilliseconds = selectionWatch.Elapsed.TotalMilliseconds,
            metadataAllocatedBytesIncludingStopwatch = selectionAllocation,
            catalogSearches = 1000, searchMilliseconds = searchWatch.Elapsed.TotalMilliseconds, matching,
            availability = states
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
