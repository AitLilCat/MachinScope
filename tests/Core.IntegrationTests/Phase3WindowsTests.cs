using System.Security.Cryptography;
using Core.Windows.Hardware;
using Core.Windows.FileSystem;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;
[TestClass] public sealed class Phase3WindowsTests
{
    [TestMethod] public async Task NativeInventoryMapsVolumesAndProtectsRunningOSDisk()
    {
        var inventory = await new DiskInspector().InventoryAsync(default); Assert.NotEmpty(inventory.Disks);
        var systemPath = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var systemDisk = inventory.Disks.Single(d => d.Partitions.Any(p => p.Volumes.Any(v => v.Path.Equals(systemPath, StringComparison.OrdinalIgnoreCase))));
        Assert.Equal(DiskProtection.Protected, systemDisk.Protection); Assert.True(systemDisk.Target.Identity.Length > 0);
        var stale = systemDisk.Target with { Capacity = systemDisk.Target.Capacity + 1 }; await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new DiskInspector().DetailsAsync(stale, default));
    }
    [TestMethod] public async Task NativeDiskPropertiesHaveRealSourcesAndConsistentColumns()
    {
        var inspector = new DiskInspector(); var inventory = await inspector.InventoryAsync(default); var details = await inspector.DetailsAsync(inventory.Disks[0].Target, default);
        Assert.Equal(6, details.Tabs.Count); Assert.All(details.Tabs, t => Assert.All(t.Rows, r => Assert.Equal(t.Columns.Length, r.Length))); Assert.Contains(details.Tabs, t => t.Title == "Health"); Assert.Contains(details.Tabs, t => t.Title == "Advanced" && t.Rows.Any(r => r[0] == "Device path"));
    }
    [TestMethod] public async Task FilePropertiesExposeNativeIdentityAllocationAclAndExplicitHashes()
    {
        var root = Path.Combine(Path.GetTempPath(), "core-properties-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "fixture.txt"); await File.WriteAllTextAsync(path, "fixture"); var inspector = new FilePropertiesInspector(); var details = await inspector.DetailsAsync(path, default);
            Assert.Equal(7L, details.Target.Length!.Value); Assert.True(details.Target.Identity.Length > 0); Assert.Contains(details.Tabs, t => t.Title == "Security" && t.Rows.Any(r => r[0] == "Owner"));
            Assert.Equal(Convert.ToHexString(SHA256.HashData("fixture"u8)), await inspector.HashAsync(details.Target, "SHA-256", default)); Assert.Equal(128, (await inspector.HashAsync(details.Target, "SHA-512", default)).Length);
            await File.WriteAllTextAsync(path, "changed fixture"); await Assert.ThrowsAnyAsync<IOException>(() => inspector.HashAsync(details.Target, "SHA-256", default));
            using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.DetailsAsync(path, cts.Token));
        }
        finally { Directory.Delete(root, true); }
    }
    [TestMethod] public void WindowsCapabilityResolutionReportsCurrentPrivileges()
    {
        var platform = new WindowsPlatform(); Assert.Equal(PlatformKind.Windows, platform.Platform.Kind); Assert.Contains(platform.Capabilities, c => c.Id == "disk.mutate" && !c.Available); Assert.True(platform.Current().User.Length > 0);
    }
}
