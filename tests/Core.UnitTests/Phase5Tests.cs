using Core.Application.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class Phase5Tests
{
    [TestMethod] public async Task RefreshBurstRetainsOnlyLatestPendingRequest()
    {
        await using var coordinator = new RefreshCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<int>();
        var pump = coordinator.Request(async _ => { calls.Add(0); started.SetResult(); await release.Task; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 1; i <= 1000; i++) { var captured = i; Assert.True(ReferenceEquals(pump, coordinator.Request(_ => { calls.Add(captured); return Task.CompletedTask; }))); }
        release.SetResult(); await pump.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal<int>([0, 1000], calls); Assert.False(coordinator.IsBusy); Assert.Equal(RefreshState.Applied, coordinator.State);
    }
    [TestMethod] public async Task ReplacementCancelsOldCollectionAndShutdownDrains()
    {
        var coordinator = new RefreshCoordinator(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancelled = false;
        var task = coordinator.Request(async token => { started.SetResult(); try { await Task.Delay(Timeout.Infinite, token); } finally { cancelled = token.IsCancellationRequested; } });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = false;
        await coordinator.Request(_ => { replacement = true; return Task.CompletedTask; }, true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancelled); Assert.True(replacement);
        await coordinator.DisposeAsync(); await coordinator.DisposeAsync();
        await coordinator.Request(_ => throw new Exception("Must never execute after shutdown"));
    }
    [TestMethod] public async Task FailedCollectionDoesNotStrandNextRefresh()
    {
        await using var coordinator = new RefreshCoordinator(); Exception? failure = null; coordinator.Failed += e => failure = e;
        await coordinator.Request(_ => throw new IOException("fixture")); Assert.True(failure is IOException);
        var ran = false; await coordinator.Request(_ => { ran = true; return Task.CompletedTask; }); Assert.True(ran);
    }
    [TestMethod] public async Task ThrowingFailureObserverDoesNotStrandCoordinator()
    {
        await using var coordinator = new RefreshCoordinator();
        coordinator.Failed += _ => throw new InvalidOperationException("observer fixture");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => coordinator.Request(_ => throw new IOException("collector fixture")));
        Assert.False(coordinator.IsBusy); var ran = false;
        await coordinator.Request(_ => { ran = true; return Task.CompletedTask; }); Assert.True(ran);
    }
    [TestMethod] public void NavigationRevisionRejectsReturnToSamePage()
    {
        var navigation = new NavigationRevision(); var a = navigation.Advance(); navigation.Advance(); var again = navigation.Advance();
        Assert.False(navigation.IsCurrent(a)); Assert.True(navigation.IsCurrent(again));
    }
    [TestMethod] public async Task ObsoleteUninterruptibleResultCannotOverwriteNewNavigation()
    {
        await using var coordinator = new RefreshCoordinator(); var navigation = new NavigationRevision(); var captured = navigation.Advance();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = "none";
        var first = coordinator.Request(async _ => { started.SetResult(); await release.Task; if (navigation.IsCurrent(captured)) applied = "old"; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); navigation.Advance();
        var latest = coordinator.Request(_ => { applied = "new"; return Task.CompletedTask; }, true);
        release.SetResult(); await latest.WaitAsync(TimeSpan.FromSeconds(5)); await first; Assert.Equal("new", applied);
    }
    [TestMethod] public void LargeSelectionSurvivesFilterAndDropsOnlyExpiredKeys()
    {
        var keys = Enumerable.Range(0, 100000).Select(i => "fixture:" + i).ToArray(); var state = new TableSelection(); state.All(keys);
        state.ReplaceVisible(keys.Take(1000), keys.Take(1000).Reverse()); state.Reconcile(keys.Reverse()); Assert.Equal(100000, state.Keys.Count);
        state.Reconcile(keys.Skip(50000)); Assert.Equal(50000, state.Keys.Count); Assert.False(state.Keys.Contains(keys[0])); Assert.True(state.Keys.Contains(keys[^1]));
    }
    [TestMethod] public void ReconciliationPreservesObjectsAndUsesBoundedNotifications()
    {
        var original = Enumerable.Range(0, 100000).Select(_ => new object()).ToArray(); var rows = new ReconciledCollection<object>(original); var events = 0;
        rows.CollectionChanged += (_, _) => events++;
        Assert.False(rows.Reconcile(original)); Assert.Equal(0, events);
        var reverse = original.Reverse().ToArray(); Assert.True(rows.Reconcile(reverse)); Assert.Equal(1, events); Assert.Equal<object>(reverse, rows);
        Assert.False(rows.Reconcile(reverse.Take(99999).ToArray())); Assert.Equal(2, events);
    }
    [TestMethod] public void SmallReordersInsertionsRemovalsMatchDesiredReferences()
    {
        var random = new Random(753); var pool = Enumerable.Range(0, 50).Select(_ => new object()).ToArray(); var rows = new ReconciledCollection<object>(pool);
        for (var repeat = 0; repeat < 300; repeat++) { var next = pool.Where(_ => random.Next(3) > 0).OrderBy(_ => random.Next()).ToArray(); rows.Reconcile(next); Assert.Equal<object>(next, rows); }
    }
    [TestMethod] public void LargeInitialLoadAndTailChangesDoNotEmitPerRowEvents()
    {
        var rows = new ReconciledCollection<object>(); var values = Enumerable.Range(0, 100000).Select(_ => new object()).ToArray(); var events = 0; rows.CollectionChanged += (_, _) => events++;
        Assert.True(rows.Reconcile(values)); Assert.Equal(1, events);
        Assert.True(rows.Reconcile(values.Take(50000).ToArray())); Assert.Equal(2, events);
        Assert.True(rows.Reconcile(values)); Assert.Equal(3, events); Assert.Equal<object>(values, rows);
    }
    [TestMethod] public async Task ClosingWaitsForNonCancellableProviderBeforeReleasingOwnership()
    {
        var coordinator = new RefreshCoordinator(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        var work = coordinator.Request(async _ => { started.SetResult(); await release.Task; finished = true; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); var close = coordinator.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted); Assert.False(finished);
        release.SetResult(); await close.WaitAsync(TimeSpan.FromSeconds(5)); await work;
        Assert.True(finished); Assert.Equal(RefreshState.Stale, coordinator.State);
    }
    [TestMethod] public async Task DiagnosticsAreOptInBoundedAndExportNoFreeText()
    {
        var diagnostics = new LocalDiagnostics(4); diagnostics.Record(DiagnosticMetric.Refresh, TimeSpan.FromSeconds(1), RefreshState.Failed); Assert.Equal(0, diagnostics.Snapshot().Length);
        diagnostics.Enabled = true;
        Parallel.For(0, 1000, _ => diagnostics.Record(DiagnosticMetric.Refresh, TimeSpan.FromMilliseconds(1), RefreshState.Applied)); Assert.Equal(4, diagnostics.Snapshot().Length);
        var file = Path.Combine(Path.GetTempPath(), "core-diag-" + Guid.NewGuid() + ".json");
        try { await diagnostics.ExportAsync(file, default); var json = await File.ReadAllTextAsync(file); Assert.Contains("\"redacted\": true", json); Assert.False(json.Contains(Environment.UserName)); }
        finally { File.Delete(file); }
        diagnostics.Enabled = false; Assert.Equal(0, diagnostics.Snapshot().Length);
    }
    [TestMethod] public void NativeErrorsHaveActionableCategories()
    {
        Assert.Equal(OperationErrorCategory.ObservationExpired, OperationErrors.Classify(new Core.Domain.Models.ObservationExpiredException("fixture")).Category);
        Assert.Equal(OperationErrorCategory.PermissionDenied, OperationErrors.Classify(new System.ComponentModel.Win32Exception(5)).Category);
        Assert.Equal(OperationErrorCategory.ResourceUnavailable, OperationErrors.Classify(new System.ComponentModel.Win32Exception(1167)).Category);
        Assert.Contains("timed out", OperationErrors.Classify(new System.ComponentModel.Win32Exception(1460)).Message);
        Assert.Contains("remain in effect", OperationErrors.Classify(new OperationCanceledException()).Message);
        Assert.Equal(OperationErrorCategory.NativeProviderFailure, OperationErrors.Classify(new System.ComponentModel.Win32Exception(13)).Category);
    }
}
