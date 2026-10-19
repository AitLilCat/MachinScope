using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
using Core.Windows.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class AdministrationCatalogTests
{
    [TestMethod] public void CatalogHasUniqueIdsAndNoPlatformSpecificToolsLeakToOtherHosts()
    {
        Assert.Equal(AdministrationCatalog.Tools.Count, AdministrationCatalog.Tools.Select(t => t.Id).Distinct().Count());
        Assert.True(AdministrationCatalog.Filter(PlatformKind.Windows).All(t => t.Platform is null or PlatformKind.Windows));
        Assert.True(AdministrationCatalog.Filter(PlatformKind.Linux).All(t => t.Platform == null));
        Assert.True(AdministrationCatalog.Filter(PlatformKind.Unsupported).All(t => t.Platform == null));
        Assert.Contains(AdministrationCatalog.Filter(PlatformKind.Linux), t => t.Id == "files.analysis");
        Assert.True(AdministrationCatalog.Tools.All(t => t.Name.Length > 0 && t.Description.Length > 0));
    }
    [TestMethod] public void SearchAndGroupFiltersUseMetadataWithoutProbingTheSystem()
    {
        var found = AdministrationCatalog.Filter(PlatformKind.Windows, "FIREWALL", AdministrationGroup.SecurityUtilities);
        Assert.Equal(2, found.Count); Assert.True(found.All(t => t.Id.StartsWith("windows.firewall")));
        Assert.Equal(0, AdministrationCatalog.Filter(PlatformKind.Windows, "firewall", AdministrationGroup.StorageUtilities).Count);
        Assert.Equal(0, AdministrationCatalog.Filter(PlatformKind.Linux, "firewall").Count);
    }
    [TestMethod] public void PlatformCapabilityImplementationAndPrivilegeStatesFailClosed()
    {
        var tool = AdministrationCatalog.Tools.Single(t => t.Id == "windows.services");
        Capability[] capabilities = [new("windows.tools", true, "Fixture", "")];
        Assert.Equal(FeatureAvailability.UnsupportedPlatform, AdministrationCatalog.Availability(tool, PlatformKind.Linux, capabilities, false, true, "").State);
        Assert.False(AdministrationCatalog.Availability(tool, PlatformKind.Windows, [], false, true, "").CanOpen);
        Assert.False(AdministrationCatalog.Availability(tool with { Implemented = false }, PlatformKind.Windows, capabilities, true, true, "").CanOpen);
        var unelevated = AdministrationCatalog.Availability(tool, PlatformKind.Windows, capabilities, false, true, "Installed");
        Assert.Equal(FeatureAvailability.RequiresElevation, unelevated.State); Assert.True(unelevated.CanOpen);
        Assert.Contains("Windows handles", unelevated.Explanation);
        Assert.False(AdministrationCatalog.Availability(tool with { EntryPoint = null }, PlatformKind.Windows, capabilities, false, true, "").CanOpen);
        Assert.Equal(FeatureAvailability.Available, AdministrationCatalog.Availability(tool, PlatformKind.Windows, capabilities, true, true, "Installed").State);
    }
    private sealed class Privilege : IPrivilegeService { public PrivilegeSnapshot Current() => new("fixture", false, "Fixture token"); }
    private sealed class FixturePlatform : IPlatformCapabilities {
        public PlatformIdentity Platform { get; } = new(PlatformKind.Windows, "Fixture", System.Runtime.InteropServices.Architecture.X64, System.Runtime.InteropServices.Architecture.X64);
        public IReadOnlyList<Capability> Capabilities { get; } = [new("windows.tools", true, "Fixture", "")];
    }
    private sealed class Runtime : IWindowsToolRuntime {
        public string SystemDirectory => Path.Combine(Path.GetTempPath(), "System fixture with spaces");
        public HashSet<string> Missing = []; public bool UriSupported = true; public string? Edition { get; set; } = "Professional";
        public List<System.Diagnostics.ProcessStartInfo> Launched = [];
        public bool Exists(string path) => !Missing.Contains(Path.GetFileName(path));
        public Task<bool> SupportsUriAsync(Uri uri) => Task.FromResult(UriSupported);
        public Task LaunchAsync(System.Diagnostics.ProcessStartInfo start) { Launched.Add(start); return Task.CompletedTask; }
    }
    [TestMethod] public async Task MissingUtilitiesUriHandlersAndHomeEditionAreNotLaunchable()
    {
        var runtime = new Runtime { UriSupported = false, Edition = "Core" }; runtime.Missing.Add("services.msc");
        await using var service = new WindowsAdministrationTools(new FixturePlatform(), new Privilege(), runtime);
        var states = await service.InspectAsync(false, default);
        foreach (var id in new[] { "windows.services", "windows.settings", "windows.localusers" }) {
            Assert.False(states.Single(s => s.Id == id).CanOpen);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await service.OpenAsync(id, default));
        }
        Assert.Equal(0, runtime.Launched.Count);
    }
    [TestMethod] public async Task LaunchUsesRegisteredTargetsSeparateArgumentsAndNormalWindowsAuthorization()
    {
        var runtime = new Runtime(); await using var service = new WindowsAdministrationTools(new FixturePlatform(), new Privilege(), runtime);
        await service.OpenAsync("windows.services", default);
        var start = runtime.Launched.Single(); Assert.Equal(Path.Combine(runtime.SystemDirectory, "mmc.exe"), start.FileName);
        Assert.Equal(1, start.ArgumentList.Count); Assert.Equal(Path.Combine(runtime.SystemDirectory, "services.msc"), start.ArgumentList[0]);
        Assert.True(start.UseShellExecute); Assert.True(string.IsNullOrEmpty(start.Verb));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.OpenAsync("services.msc & injected-command", default));
        Assert.Equal(1, runtime.Launched.Count);
    }
    [TestMethod] public async Task LaunchRechecksAvailabilityInsteadOfTrustingCachedCatalogResults()
    {
        var runtime = new Runtime(); await using var service = new WindowsAdministrationTools(new FixturePlatform(), new Privilege(), runtime);
        Assert.True((await service.InspectAsync(false, default)).Single(s => s.Id == "windows.services").CanOpen);
        runtime.Missing.Add("services.msc");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await service.OpenAsync("windows.services", default));
        Assert.Equal(0, runtime.Launched.Count);
    }
}
