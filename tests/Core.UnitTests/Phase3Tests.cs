using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;
[TestClass] public sealed class Phase3Tests
{
    private sealed class ActionFixture : ICoreAction<string>
    {
        public ActionRisk Risk => ActionRisk.Destructive;
        public string Name => "Fixture";
        public List<string> Calls { get; } = [];
        public string Identity(string target) => target;
        public ActionDecision Validate(string target) => target == "denied" ? ActionDecision.Deny("Fixture policy") : ActionDecision.Permit;
        public Task<string> ExecuteAsync(string target, CancellationToken token) { Calls.Add(target); if (target == "failed") throw new IOException("Fixture failure"); return Task.FromResult("OK"); }
    }
    [TestMethod] public async Task ActionsRequireExactConfirmationAndRevalidateEachItem()
    {
        var action = new ActionFixture(); var results = await new ActionService().ExecuteAsync(action, new[] { "unconfirmed", "denied", "failed", "good" }, new HashSet<string> { "denied", "failed", "good" }, null, default);
        Assert.Equal(new[] { ActionOutcome.Denied, ActionOutcome.Denied, ActionOutcome.Failed, ActionOutcome.Succeeded }.AsEnumerable(), results.Select(r => r.Outcome));
        Assert.Equal(new[] { "failed", "good" }.AsEnumerable(), action.Calls);
    }
    [TestMethod] public async Task CancellationKeepsResultsAndDoesNotStartFurtherActions()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel(); var action = new ActionFixture(); var results = await new ActionService().ExecuteAsync(action, new[] { "a", "b" }, new HashSet<string> { "a", "b" }, null, cts.Token);
        Assert.Equal(2, results.Count); Assert.All(results, r => Assert.Equal(ActionOutcome.Cancelled, r.Outcome)); Assert.Equal(0, action.Calls.Count);
    }
    [TestMethod] public void SystemDiskProtectionAppliesToTheWholeDisk()
    {
        var data = new PartitionTarget(0, 1, "data", "Basic data", 0, 100, false, []);
        var efi = data with { Number = 2, DeviceId = "EFI", Type = "GPT: EFI System" };
        Assert.Equal(DiskProtection.Protected, DiskProtectionPolicy.Classify(false, false, [data, efi], true).State);
        Assert.Equal(DiskProtection.Protected, DiskProtectionPolicy.Classify(true, false, [data], true).State);
        Assert.Equal(DiskProtection.Unknown, DiskProtectionPolicy.Classify(false, false, [data], false).State);
    }
    [TestMethod] public void PagefileAndRecoveryAreProtectedAndUnknownIsNotWritable()
    {
        var volume = new VolumeTarget("D:\\", "volume-id", "Data", "NTFS", 100, 10, false, false, true);
        var partition = new PartitionTarget(1, 1, "partition", "Basic data", 0, 100, false, [volume]);
        Assert.Equal(DiskProtection.Protected, DiskProtectionPolicy.Classify(false, false, [partition], true).State);
        Assert.Equal(DiskProtection.Protected, DiskProtectionPolicy.Classify(false, false, [partition with { Type = "Recovery", Volumes = [] }], true).State);
        Assert.Equal(DiskProtection.Unknown, DiskProtectionPolicy.Classify(null, false, [], true).State);
        var disk = new InspectedDisk(new(1, "device", "model", "serial", 100, "uid"), "", "", "", [], DiskProtection.DataCandidate, [], new Dictionary<string, string>());
        Assert.False(DiskProtectionPolicy.ManagementDecision(disk).Allowed);
    }
    [TestMethod] public void BrowseHistoryPrunesForwardBranchAndRemainsBounded()
    {
        var history = new BrowseHistory("a", 3); history.Navigate("b"); history.Navigate("c"); Assert.Equal("b", history.Back()); history.Navigate("d"); Assert.False(history.CanForward); Assert.Equal("b", history.Back()); Assert.Equal("d", history.Forward()); history.Navigate("e"); history.Back(); Assert.Equal("b", history.Back()); Assert.False(history.CanBack);
    }
    [TestMethod] public void DateSearchDistinguishesAgeFromCalendarDate()
    {
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var row = new InspectionRow("file", ["backup.log"], ["backup.log"], new Dictionary<string, object?> { ["modified"] = now.AddDays(-10).ToString("O"), ["created"] = now.AddDays(-40).ToString("O") });
        Assert.True(SearchQuery.Parse("modified<30d created>30d", now).Matches(row)); Assert.False(SearchQuery.Parse("modified>30d", now).Matches(row)); Assert.True(SearchQuery.Parse("modified>=2026-09-01", now).Matches(row)); Assert.Throws<FormatException>(() => SearchQuery.Parse("modified<yesterday", now));
    }
    [TestMethod] public void RuntimeIdentityMatchesCurrentSupportedOS()
    {
        var identity = PlatformIdentity.Current(); Assert.Equal(OperatingSystem.IsWindows() ? PlatformKind.Windows : OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Unsupported, identity.Kind); Assert.True(identity.Description.Length > 0);
    }
    [TestMethod] public void ConnectionTimelineKeepsIdentityAcrossStateChangesAndBoundsEvents()
    {
        var tracker = new ConnectionTracker(); var now = DateTimeOffset.Now;
        var row = new ConnectionRow("TCP4", "127.0.0.1", 443, "127.0.0.1", 50000, "Established", 42, "fixture") { OwnerStarted = DateTime.Today, NativeCreated = 100 };
        var first = tracker.Update([row], now)[0]; var changed = tracker.Update([row with { State = "CloseWait" }], now.AddSeconds(1))[0];
        Assert.Equal(first.ObservationId, changed.ObservationId); Assert.Equal(ConnectionEventKind.StateChanged, tracker.Events[1].Kind); tracker.Update([], now.AddSeconds(2)); Assert.Equal(ConnectionEventKind.Disappeared, tracker.Events[2].Kind);
        tracker.Update([row with { OwnerStarted = null, NativeCreated = null }], now.AddSeconds(3)); Assert.Equal(3, tracker.Events.Count);
        for (var i = 0; i < 2100; i++) tracker.Update([row with { NativeCreated = i + 1000 }], now.AddSeconds(i + 4));
        Assert.Equal(2048, tracker.Events.Count); Assert.True(tracker.Events[0].Sequence > 1);
    }
}
