using System.Diagnostics;
using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
using Core.Windows;
using Core.Windows.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class Phase75IntegrationTests
{
    [TestMethod] public void RealWindowsDetectionIncludesActualHostVersionAndArchitecture()
    {
        var platform = new PlatformInformation().Platform; Assert.Equal(PlatformKind.Windows, platform.Kind);
        Assert.Equal(Environment.MachineName, platform.Hostname); Assert.Equal(Environment.OSVersion.VersionString, platform.Version);
        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture, platform.OSArchitecture);
        Assert.Equal(new WindowsPlatform().Current().User, Environment.UserDomainName + "\\" + Environment.UserName);
    }
    [TestMethod] public async Task InstalledWindowsToolsPrepareValidLaunchTargetsWithoutOpeningWindows()
    {
        var platform = new WindowsPlatform(); await using var service = new WindowsAdministrationTools(platform, platform);
        var states = await service.InspectAsync(true, default); Assert.NotEmpty(states);
        foreach (var state in states.Where(s => s.CanOpen)) {
            var tool = AdministrationCatalog.Tools.Single(t => t.Id == state.Id); if (tool.EntryPoint == null) continue;
            var start = await service.PrepareAsync(tool.Id, default);
            Assert.True(start.UseShellExecute); Assert.True(string.IsNullOrEmpty(start.Verb));
            if (tool.EntryPoint.Kind != EntryPointKind.SettingsUri) {
                Assert.True(File.Exists(start.FileName)); foreach (var argument in start.ArgumentList) Assert.True(File.Exists(argument));
            }
        }
        var before = await service.InspectAsync(false, default); Assert.True(ReferenceEquals(states, before));
    }
    [TestMethod] public async Task PlatformAndAdministrationAliasesDisposeThroughTheApplicationLifetime()
    {
        var collection = new ServiceCollection(); collection.AddSingleton<ILogger<SystemInspector>>(NullLogger<SystemInspector>.Instance); collection.AddCoreWindows();
        await using var services = collection.BuildServiceProvider();
        Assert.True(ReferenceEquals(services.GetRequiredService<IPlatformInformation>(), services.GetRequiredService<IPlatformCapabilities>()));
        var tools = services.GetRequiredService<IAdministrationTools>(); Assert.NotEmpty(await tools.InspectAsync(false, default));
    }
    [TestMethod] public async Task TerminatedProcessInspectionKeepsOriginalIdentityRatherThanThrowingOrRejoining()
    {
        var inspector = new SystemInspector(NullLogger<SystemInspector>.Instance);
        using var process = await ProcessTestEnvironment.StartOwnedFixture();
        try {
            var child = inspector.Processes().Single(p => p.Id == process.Id);
            inspector.EndProcess(child, false); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var result = await inspector.DetailsAsync(child, default);
            Assert.Contains(result.General.Rows, r => r[0] == "Started" && r[1] == child.Started?.ToString("O"));
            Assert.Contains(result.General.Rows, r => r[0] == "Inspection status" && r[1].Contains("snapshot only"));
            Assert.Equal("Status", result.Modules.Columns[0]);
        } finally { await ProcessTestEnvironment.StopOwnedFixture(process); }
    }
    [TestMethod] public async Task DiskLayoutRetainsTypedVolumeAndPartitionRelationships()
    {
        var inspector = new DiskInspector(); var inventory = await inspector.InventoryAsync(default); Assert.NotEmpty(inventory.Disks);
        var detail = await inspector.DetailsAsync(inventory.Disks[0].Target, default);
        var layout = detail.Tabs.Single(t => t.Title == "Layout"); Assert.True(layout.Data != null);
        Assert.True(layout.Data!.All(r => r.Subject is VolumeTarget or PartitionTarget));
        Assert.Equal(layout.Data!.Count, layout.Data!.Select(r => r.Key).Distinct().Count());
    }
}
