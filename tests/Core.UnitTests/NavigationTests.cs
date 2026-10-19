using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class NavigationTests
{
    private static Capability[] Providers => NavigationRegistry.Routes.Where(r => r.Capability != null).Select(r => new Capability(r.Capability!, true, "Fixture", "Available")).DistinctBy(c => c.Id).ToArray();
    [TestMethod] public void RegistrationHasUniqueStableIdsAndAllEnumDestinations()
    {
        Assert.Equal(NavigationRegistry.Routes.Count, NavigationRegistry.Routes.Select(r => r.Id).Distinct().Count());
        Assert.Equal<NavigationDestination>(Enum.GetValues<NavigationDestination>().Distinct().Order(), NavigationRegistry.Routes.Select(r => r.Destination).Order());
        Assert.Equal(5, NavigationRegistry.Groups.Count);
        foreach (var group in NavigationRegistry.Groups) { var homepage = NavigationRegistry.Get(group.Homepage); Assert.True(homepage.Homepage); Assert.Equal(group.Category, homepage.Category!.Value); }
    }
    [TestMethod] public void ChevronExpansionIsIndependentOfSelectedDestination()
    {
        var state = new NavigationCoordinator(PlatformKind.Windows, Providers);
        Assert.True(state.Select(NavigationDestination.PhysicalDisks)); Assert.True(state.IsExpanded(NavigationCategory.Hardware));
        state.SetExpanded(NavigationCategory.Hardware, false); Assert.Equal(NavigationDestination.PhysicalDisks, state.Selected);
        state.SetExpanded(NavigationCategory.Network, true); Assert.Equal(NavigationDestination.PhysicalDisks, state.Selected);
        Assert.True(state.Select(NavigationDestination.HardwareOverview)); Assert.False(state.IsExpanded(NavigationCategory.Hardware));
    }
    [TestMethod] public void RestoreRevealsSelectedParentAndRetainsPane()
    {
        var state = new NavigationCoordinator(PlatformKind.Windows, Providers);
        state.Select(NavigationDestination.Files); state.SetExpanded(NavigationCategory.Hardware, false); state.SetExpanded(NavigationCategory.Activity, true); state.PaneOpen = false;
        var restored = new NavigationCoordinator(PlatformKind.Windows, Providers, state.Snapshot());
        Assert.Equal(NavigationDestination.Files, restored.Selected); Assert.True(restored.IsExpanded(NavigationCategory.Hardware)); Assert.True(restored.IsExpanded(NavigationCategory.Activity)); Assert.False(restored.PaneOpen);
        restored.PaneOpen = true; Assert.Equal(NavigationDestination.Files, restored.Selected);
    }
    [TestMethod] public void UnknownPlannedAndMissingProvidersRestoreToCoreOverview()
    {
        foreach (var id in new[] { "removed.route", "activity.services", "activity.processes" }) Assert.Equal(NavigationDestination.CoreOverview, new NavigationCoordinator(PlatformKind.Windows, [], new(id, ["unknown"])).Selected);
        var state = new NavigationCoordinator(PlatformKind.Windows, []);
        Assert.False(state.Select(NavigationDestination.Policies)); Assert.False(state.Select(NavigationDestination.Devices)); Assert.Equal(NavigationDestination.CoreOverview, state.Selected);
        Assert.True(state.Select(NavigationDestination.HardwareOverview));
    }
    [TestMethod] public void PlatformRequirementCannotBeSatisfiedByAnUnrelatedProvider()
    {
        var route = NavigationRegistry.Get(NavigationDestination.Startup);
        Assert.False(NavigationRegistry.Availability(route, PlatformKind.Linux, Providers).Allowed);
        Assert.True(NavigationRegistry.Availability(NavigationRegistry.Get(NavigationDestination.Files), PlatformKind.Linux, Providers).Allowed);
        Assert.Equal(NavigationDestination.CoreOverview, new NavigationCoordinator(PlatformKind.Linux, Providers, new(route.Id)).Selected);
    }
    [TestMethod] public async Task RapidNavigationCancelsSlowWorkAndOnlyLatestRevisionApplies()
    {
        var state = new NavigationCoordinator(PlatformKind.Windows, Providers); var revision = new NavigationRevision();
        await using var refresh = new RefreshCoordinator(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<NavigationDestination>(); var captured = revision.Advance(); var cancelled = false;
        state.Select(NavigationDestination.Files);
        var pump = refresh.Request(async token => { started.SetResult(); await release.Task; cancelled = token.IsCancellationRequested; if (!token.IsCancellationRequested && revision.IsCurrent(captured)) applied.Add(NavigationDestination.Files); });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 500; i++) { var destination = i == 499 ? NavigationDestination.Connections : NavigationDestination.Processes; state.Select(destination); var generation = revision.Advance(); _ = refresh.Request(_ => { if (revision.IsCurrent(generation)) applied.Add(destination); return Task.CompletedTask; }, true); }
        release.SetResult(); await pump.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancelled); Assert.Equal<NavigationDestination>([NavigationDestination.Connections], applied); Assert.Equal(NavigationDestination.Connections, state.Selected); Assert.False(refresh.IsBusy);
    }
    [TestMethod] public void CategoryVisitsAndPaneChangesHaveNoCollectorOrToolExecution()
    {
        var state = new NavigationCoordinator(PlatformKind.Windows, Providers);
        for (var i = 0; i < 1000; i++) foreach (var group in NavigationRegistry.Groups) { state.Select(group.Homepage); state.SetExpanded(group.Category, false); state.PaneOpen = !state.PaneOpen; }
        Assert.Equal(NavigationDestination.Tools, state.Selected);
        foreach (var route in NavigationRegistry.Routes.Where(r => r.Homepage)) Assert.Equal(route.Destination == NavigationDestination.ActivityOverview ? NavigationRefresh.ActivitySummary : NavigationRefresh.SummaryOnly, route.Refresh);
        Assert.Equal(NavigationRefresh.SummaryOnly, NavigationRegistry.Get(NavigationDestination.Files).Refresh);
        Assert.Equal(NavigationRefresh.SummaryOnly, NavigationRegistry.Get(NavigationDestination.PhysicalDisks).Refresh);
    }
    [TestMethod] public void ToolsHaveUniqueIdsAndMutationContractsWithoutExecutableDelegates()
    {
        Assert.Equal(ToolRegistry.Tools.Count, ToolRegistry.Tools.Select(t => t.Id).Distinct().Count());
        foreach (var tool in ToolRegistry.Tools.Where(t => t.Mutating)) { var r = tool.Requirements!; Assert.True(r.ExactIdentity && r.InspectCurrentState && r.Preview && r.VerifyPrivilege && r.Confirm && r.VerifyResult && r.StructuredResults); Assert.True(r.RecoveryGuidance.Length > 0); Assert.True(tool.Level != ToolLevel.Inspect); }
        foreach (var tool in ToolRegistry.Tools.Where(t => t.Level == ToolLevel.Advanced && t.Mutating)) Assert.False(ToolRegistry.Availability(tool, PlatformKind.Windows, Providers).Allowed);
        Assert.False(ToolRegistry.Availability(ToolRegistry.Tools.Single(t => t.Id == "startup.configure"), PlatformKind.Linux, Providers).Allowed);
    }
}
