using Core.Domain.Models;
using Core.Windows.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class InspectionPatchTests
{
    [TestMethod] public void ManualWidthsPersistForLiveTablesIncludingWideColumns()
    {
        var root = Path.Combine(Path.GetTempPath(), "core-column-patch-" + Guid.NewGuid());
        try {
            var store = new Core.Persistence.Settings.PreferencesStore(root);
            var titles = new[] { "Processes", "Disk health", "Startup", "Interface traffic" };
            store.Save(new Preferences { Tables = titles.ToDictionary(t => t, _ => new ColumnLayout[] { new("Name", 141, true, 1), new("Value", 2900, true, 0) }) });
            var restored = new Core.Persistence.Settings.PreferencesStore(root).Load();
            foreach (var title in titles) { Assert.Equal(141d, restored.Tables[title][0].Width); Assert.Equal(2900d, restored.Tables[title][1].Width); Assert.Equal(1, restored.Tables[title][0].Order); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [TestMethod] public async Task MissingOrReusedIdentityReturnsSelectedSnapshotWhileControlsStillRefuseIt()
    {
        var inspector = new SystemInspector(NullLogger<SystemInspector>.Instance);
        var self = inspector.Processes().Single(p => p.Id == Environment.ProcessId);
        foreach (var selected in new[] { self with { Started = null, Path = "frozen-unverified.exe" }, self with { Started = DateTime.MinValue, Path = "frozen-stale.exe" } }) {
            var details = await inspector.DetailsAsync(selected, default);
            Assert.Contains(details.General.Rows, r => r[0] == "Executable" && r[1] == selected.Path);
            Assert.Contains(details.General.Rows, r => r[0] == "Inspection status" && r[1].Contains("snapshot only"));
            Assert.Equal("Status", details.Threads.Columns[0]);
            Assert.Throws<InvalidOperationException>(() => inspector.EndProcess(selected, false));
        }
    }
    [TestMethod] public async Task CancelledInspectionDoesNotBecomeAnUnavailableResult()
    {
        var inspector = new SystemInspector(NullLogger<SystemInspector>.Instance);
        var sample = new ProcessSample(0, 0, "Idle", null, null, null, null, null, null, null, "Unavailable", null, null, null);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await inspector.DetailsAsync(sample, cancelled.Token));
    }
}
