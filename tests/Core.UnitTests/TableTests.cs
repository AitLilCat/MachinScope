using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;
[TestClass] public sealed class TableTests
{
    private static InspectionRow Row(string key, string name, long bytes) => new(key, [name, bytes.ToString()], [name, bytes.ToString()], new Dictionary<string, object?> { ["name"] = name, ["memory"] = bytes, ["cpu"] = 12.5, ["pid"] = 42, ["protocol"] = "TCP6", ["localport"] = 50000, ["remoteport"] = 443 });
    [TestMethod] public void SelectionUsesIdentityThroughReorderFilterAndReuse()
    {
        var state = new TableSelection(); state.Click(["42:old", "43:a", "44:a"], "42:old", false, false); state.Click(["42:old", "43:a", "44:a"], "44:a", true, false); state.ReplaceVisible(["43:a", "44:a"], ["44:a"]); Assert.True(state.Keys.Contains("42:old")); state.Reconcile(["44:a", "42:new", "43:a"]); Assert.False(state.Keys.Contains("42:new")); Assert.False(state.Keys.Contains("42:old")); Assert.True(state.Keys.Contains("44:a"));
    }
    [TestMethod] public void RangeToggleAndSelectAllUseVisibleKeys()
    {
        var state = new TableSelection(); state.Click(["a", "b", "c", "d"], "b", false, false); state.Click(["a", "b", "c", "d"], "d", false, true); Assert.Equal<string>(["b", "c", "d"], state.Keys.Order()); state.Click(["a", "b", "c", "d"], "c", true, false); Assert.False(state.Keys.Contains("c")); state.All(["a", "c"]); Assert.Equal(4, state.Keys.Count);
    }
    [TestMethod] public void AdvancedQueryCombinesFieldsUnitsAndQuotedText()
    {
        var row = Row("a", "large process", 600_000_000); Assert.True(SearchQuery.Parse("cpu>10 memory>500MB name:\"large process\" pid=42 protocol:tcp port:443 remote:443").Matches(row)); Assert.False(SearchQuery.Parse("memory>600MiB").Matches(row)); Assert.False(SearchQuery.Parse("pid=43").Matches(row)); Assert.Throws<FormatException>(() => SearchQuery.Parse("cpu>garbage"));
    }
    [TestMethod] public void SortUsesExactLargeNumbersAndMultipleColumns()
    {
        var rows = new[] { Row("b", "same", 9_007_199_254_740_993), Row("a", "same", 9_007_199_254_740_992), Row("c", "alpha", 1) }; var sorted = TableRows.FilterSort(rows, "", [new(0, false), new(1, false)], default); Assert.Equal<string>(["c", "a", "b"], sorted.Select(r => r.Key));
    }
    [TestMethod] public void ClipboardQuotesTabsNewlinesUnicodeAndColumnOrder()
    {
        var row = Row("a", "λ\t\"quoted\"\nvalue", 42); var tsv = TableRows.Tsv([row], ["Name", "Bytes"], [1, 0], true); Assert.True(tsv.StartsWith("Bytes\tName")); Assert.Contains("42\t\"λ\t\"\"quoted\"\"\nvalue\"", tsv);
    }
    [TestMethod] public void ConnectionIdentityChangesOnPidReuseNativeRebindAndDisappearance()
    {
        var tracker = new ConnectionTracker(); var row = new ConnectionRow("TCP4", "127.0.0.1", 50000, "127.0.0.1", 443, "Established", 42, "fixture") { OwnerStarted = DateTime.Now, NativeCreated = 123 }; var time = DateTimeOffset.Now;
        var first = tracker.Update([row], time)[0]; Assert.Equal(first.ObservationId, tracker.Update([row with { State = "CloseWait" }], time.AddSeconds(1))[0].ObservationId); Assert.NotEqual(first.ObservationId, tracker.Update([row with { NativeCreated = 124 }], time)[0].ObservationId); Assert.NotEqual(first.ObservationId, tracker.Update([row with { OwnerStarted = row.OwnerStarted?.AddSeconds(1) }], time)[0].ObservationId); tracker.Update([], time); Assert.NotEqual(first.ObservationId, tracker.Update([row], time)[0].ObservationId); var unreadable = row with { OwnerStarted = null, NativeCreated = null }; Assert.NotEqual(tracker.Update([unreadable], time)[0].ObservationId, tracker.Update([unreadable], time)[0].ObservationId);
    }
    [TestMethod] public async Task JsonExportPreservesRawValuesAndSnapshotIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"); try { var row = Row("stable", "λ", 9_007_199_254_740_993); await ExportService.WriteRowsAsync(path, "test", ["Name", "Bytes"], [row], [1, 0], default); using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path)); var first = json.RootElement.GetProperty("rows")[0]; Assert.Equal("stable", first.GetProperty("identity").GetString()); Assert.Equal("9007199254740993", first.GetProperty("raw")[0].GetString()); } finally { File.Delete(path); }
    }
    [TestMethod] public void TreeExcludesChildrenOlderThanReusedParentAndRequiresCurrentRoot()
    {
        var start = DateTime.Now; ProcessSample P(int id, int parent, DateTime began) => new(id, parent, "fixture", began, null, null, null, null, null, null, "Normal", null, null, null); var root = P(10, 0, start); var child = P(11, 10, start.AddSeconds(1)); var old = P(12, 10, start.AddSeconds(-1)); var snapshot = new[] { root, child, old }; Assert.Equal<string>([child.Identity, root.Identity], ProcessTree.ObservedDescendants(root, snapshot).Select(p => p.Identity)); Assert.Throws<InvalidOperationException>(() => ProcessTree.ObservedDescendants(root with { Started = start.AddSeconds(10) }, snapshot));
    }
}
