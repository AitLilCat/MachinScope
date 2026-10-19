using Core.Domain.Models;
using Core.Windows.Devices;
using Core.Windows.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class DeviceWindowsTests
{
    [TestMethod] public async Task NativePresentInventoryHasUniqueStableIdentitiesAndNotifications()
    {
        using var inspector = new WindowsDeviceInspector(new DiskInspector());
        var first = await inspector.SnapshotAsync(false, default); Assert.NotEmpty(first.Devices);
        Assert.Equal(first.Devices.Count, first.Devices.Select(d => d.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(first.Devices, d => { Assert.True(d.Target != null); Assert.Equal(d.Fingerprint, d.Target!.Fingerprint); Assert.True(d.InstanceId.Length > 0); });
        Assert.Contains("Native PnP notifications active", first.NotificationStatus);
        var again = await inspector.SnapshotAsync(false, default);
        Assert.Equal(first.EnumerationCount, again.EnumerationCount);
        Assert.Equal(first.Devices.First().Target, again.Devices.First(d => d.InstanceId == first.Devices.First().InstanceId).Target);
    }
    [TestMethod] public async Task LazyNativePropertiesContainDriverTopologyAndHonestMissingUsbFields()
    {
        using var inspector = new WindowsDeviceInspector(new DiskInspector());
        var inventory = await inspector.SnapshotAsync(true, default);
        // A stable root avoids any dependency on optional plugged-in USB fixtures.
        var target = inventory.Devices.FirstOrDefault(d => d.InstanceId.Equals("HTREE\\ROOT\\0", StringComparison.OrdinalIgnoreCase)) ?? inventory.Devices.First(d => !d.UsbRelated);
        var details = await inspector.DetailsAsync(target.Target!, default);
        Assert.Equal(target.Target, details.Target);
        Assert.Contains(details.Tabs, t => t.Title == "Driver"); Assert.Contains(details.Tabs, t => t.Title == "Topology");
        Assert.Equal("Unavailable", details.Tabs.Single(t => t.Title == "Connection").Rows.Single(r => r[0] == "Negotiated speed")[1]);
        Assert.Contains(details.Tabs.Single(t => t.Title == "General").Rows, r => r[0] == "Instance" && r[1] == target.InstanceId);
    }
    [TestMethod] public async Task NativeDetailsRejectStaleObservationOrFingerprint()
    {
        using var inspector = new WindowsDeviceInspector(new DiskInspector());
        var snapshot = await inspector.SnapshotAsync(true, default); var target = snapshot.Devices.First().Target!;
        await Assert.ThrowsAnyAsync<IOException>(() => inspector.DetailsAsync(target with { ObservationId = "stale" }, default));
        await Assert.ThrowsAnyAsync<IOException>(() => inspector.DetailsAsync(target with { Fingerprint = "replacement" }, default));
    }
    [TestMethod] public async Task CancelledDiscoveryDoesNotCreatePartialInventoryAndDisposalIsIdempotent()
    {
        var inspector = new WindowsDeviceInspector(new DiskInspector());
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.SnapshotAsync(true, cancel.Token));
        var snapshot = await inspector.SnapshotAsync(false, default); Assert.Equal(1L, snapshot.EnumerationCount);
        inspector.Dispose(); inspector.Dispose();
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => inspector.SnapshotAsync(false, default));
    }
}
