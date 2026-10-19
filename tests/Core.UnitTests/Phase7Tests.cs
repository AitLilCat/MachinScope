using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class Phase7Tests
{
    private static Capability[] Providers => NavigationRegistry.Routes.Where(r => r.Capability != null).Select(r => new Capability(r.Capability!, true, "Fixture", "")).DistinctBy(c => c.Id).ToArray();
    [TestMethod] public void RegistrationRejectsDuplicateAndOrphanedRoutes()
    {
        Assert.Throws<ArgumentException>(() => NavigationRegistry.Validate(NavigationRegistry.Routes.Concat([NavigationRegistry.Routes[0]]), NavigationRegistry.Groups));
        Assert.Throws<ArgumentException>(() => NavigationRegistry.Validate(NavigationRegistry.Routes.Select(r => r.Destination == NavigationDestination.Preferences ? r with { Category = (NavigationCategory)999 } : r), NavigationRegistry.Groups));
    }
    [TestMethod] public void CanonicalRoutesHaveOneHomeAndOldIdentifiersMigrate()
    {
        foreach (var id in new[] { "hardware.disks", "hardware.volumes" }) Assert.Equal(NavigationDestination.DisksVolumes, NavigationRegistry.Resolve(id)!.Destination);
        Assert.Equal(NavigationDestination.Startup, NavigationRegistry.Resolve("configuration.startup")!.Destination);
        Assert.Equal(NavigationCategory.Activity, NavigationRegistry.Get(NavigationDestination.Startup).Category!.Value);
        Assert.Equal(NavigationDestination.Tools, NavigationRegistry.Resolve("configuration.tools")!.Destination);
        Assert.Equal(NavigationCategory.Advanced, NavigationRegistry.Get(NavigationDestination.Tools).Category!.Value);
        Assert.Equal(1, NavigationRegistry.Routes.Count(r => r.LegacyPage == "Storage"));
        Assert.False(NavigationRegistry.Get(NavigationDestination.DiskImages).Implemented);
    }
    [TestMethod] public void LabelNavigationDoesNotExpandButNestedSelectionRevealsItsCategory()
    {
        var navigation = new NavigationCoordinator(PlatformKind.Windows, Providers);
        navigation.Select(NavigationDestination.HardwareOverview); Assert.False(navigation.IsExpanded(NavigationCategory.Hardware));
        navigation.Select(NavigationDestination.Files); Assert.True(navigation.IsExpanded(NavigationCategory.Hardware));
        navigation.SetExpanded(NavigationCategory.Hardware, false); Assert.Equal(NavigationDestination.Files, navigation.Selected);
    }
    [TestMethod] public void RouteHistoryBackForwardBranchingAndBoundsRemainPredictable()
    {
        var navigation = new NavigationCoordinator(PlatformKind.Windows, Providers);
        navigation.Select(NavigationDestination.Processes); navigation.Select(NavigationDestination.Files); navigation.Select(NavigationDestination.Files);
        Assert.True(navigation.Back()); Assert.Equal(NavigationDestination.Processes, navigation.Selected);
        Assert.True(navigation.Forward()); Assert.Equal(NavigationDestination.Files, navigation.Selected);
        navigation.Back(); navigation.Select(NavigationDestination.Connections); Assert.False(navigation.CanForward);
        for (var i = 0; i < 500; i++) navigation.Select(i % 2 == 0 ? NavigationDestination.Files : NavigationDestination.Processes);
        var count = 0; while (navigation.Back()) count++; Assert.Equal(99, count); Assert.False(navigation.Back());
    }
    [TestMethod] public void CapabilityStatusesFailClosedAndCannotInventAnImplementation()
    {
        var registry = new CapabilityRegistry([new("real", true, "Fixture", ""), new("planned", false, "None", "Not implemented")]);
        foreach (var state in new[] { CapabilityStatus.RequiresElevation, CapabilityStatus.TemporarilyUnavailable, CapabilityStatus.Failed, CapabilityStatus.Unsupported })
        { registry.Report("real", state, "Diagnostic"); Assert.False(CapabilityRegistry.Decision("real", registry.Snapshot).Allowed); Assert.Contains("Diagnostic", CapabilityRegistry.Decision("real", registry.Snapshot).Reason); }
        registry.Report("real", CapabilityStatus.Supported, "Recovered"); Assert.True(CapabilityRegistry.Decision("real", registry.Snapshot).Allowed);
        Assert.Throws<InvalidOperationException>(() => registry.Report("planned", CapabilityStatus.Supported, "Fake"));
        Assert.Throws<ArgumentException>(() => new CapabilityRegistry([new("duplicate", true, "", ""), new("duplicate", true, "", "")]));
    }
    private sealed record Snapshot(int Value);
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    [TestMethod] public async Task SnapshotExpiryUsesMonotonicTimeAndFailuresRetainTheLastGoodValue()
    {
        var clock = new Clock(); await using var cache = new AsyncSnapshotCache<Snapshot>(TimeSpan.FromSeconds(1), clock); var calls = 0;
        Task<Snapshot> Collect(CancellationToken _) => Task.FromResult(new Snapshot(++calls));
        var original = await cache.ReadAsync(Collect, false, default); clock.Now = 999;
        Assert.True(ReferenceEquals(original, await cache.ReadAsync(Collect, false, default)));
        await Assert.ThrowsAnyAsync<IOException>(async () => await cache.ReadAsync(_ => throw new IOException("Provider failed"), true, default));
        Assert.True(ReferenceEquals(original, cache.Last)); clock.Now = 1001;
        Assert.Equal(2, (await cache.ReadAsync(Collect, false, default)).Value);
    }
    [TestMethod] public async Task SnapshotCacheCoalescesConcurrentReadersAndExplicitRefreshReplacesIt()
    {
        await using var cache = new AsyncSnapshotCache<Snapshot>(TimeSpan.FromMinutes(1)); var calls = 0;
        async Task<Snapshot> Collect(CancellationToken token) { await Task.Delay(5, token); return new(Interlocked.Increment(ref calls)); }
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => cache.ReadAsync(Collect, false, default)));
        Assert.Equal(1, calls); Assert.True(results.All(s => ReferenceEquals(s, results[0])));
        var fresh = await cache.ReadAsync(Collect, true, default); Assert.Equal(2, fresh.Value); Assert.False(ReferenceEquals(fresh, results[0]));
    }
    [TestMethod] public async Task CancelledSnapshotCannotReplaceLastGoodResultAndDisposeDrains()
    {
        var cache = new AsyncSnapshotCache<Snapshot>(TimeSpan.FromMinutes(1)); var original = await cache.ReadAsync(_ => Task.FromResult(new Snapshot(1)), false, default);
        using var cancellation = new CancellationTokenSource(); var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var pending = cache.ReadAsync(async _ => { started.SetResult(); await release.Task; return new Snapshot(2); }, true, cancellation.Token);
        await started.Task; cancellation.Cancel(); var disposal = cache.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
        release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); await disposal;
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await cache.ReadAsync(_ => Task.FromResult(original), false, default));
    }
    [TestMethod] public void HomepagesUseRealSamplesAndExplicitlyMarkUninspectedFields()
    {
        var empty = new OverviewSnapshot(null, [], null, null, null, null, null);
        var overview = OverviewSections.Build(NavigationDestination.CoreOverview, empty);
        Assert.Contains(overview, s => s.Detail.Contains("Not inspected"));
        Assert.True(overview.All(s => NavigationRegistry.Get(s.Destination).Implemented));
        var sample = new SystemSample(DateTimeOffset.Now, 23, 16000, 4000, 0, 0, 42, TimeSpan.FromHours(5), 100, 200);
        var populated = OverviewSections.Build(NavigationDestination.ActivityOverview, empty with { Sample = sample });
        Assert.Contains(populated, s => s.Detail.Contains("42")); Assert.Contains(populated, s => s.Detail.Contains("23"));
        Assert.Equal(0, OverviewSections.Build(NavigationDestination.Tools, empty).Count);
    }
    [TestMethod] public void TrafficProjectionPreservesIdentityRawCountersAndUnsupportedNotice()
    {
        var raw = Enumerable.Range(0, 19).Select(i => i.ToString()).ToArray(); var row = new InspectionRow("adapter-id", raw, raw, new Dictionary<string, object?>());
        var traffic = NetworkTables.Traffic(new("Adapters", raw, [raw], Data: [row]));
        Assert.Equal("adapter-id", traffic.Data![0].Key); Assert.Equal("15", traffic.Data[0].Raw[2]); Assert.Equal("11", traffic.Data[0].Raw[6]);
        Assert.Contains("unsupported", traffic.Notice!); Assert.Equal(11, traffic.Columns.Length);
    }
}
