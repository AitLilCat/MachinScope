using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class DeviceTests
{
    private static DeviceRecord Device(string id = "USB\\VID_046D&PID_C534\\fixture", string parent = "", string enumerator = "USB") =>
        new(id, "Fixture", "Fixture vendor", "HIDClass", "{00000000-0000-0000-0000-000000000001}", enumerator, "hidusb", "driver", parent, "Port fixture", ["USB\\VID_046D&PID_C534&REV_2900"], [], "container", 8, 0, true);
    [TestMethod] public void RepeatedInventoryKeepsIdentityAndDoesNotManufactureConnectEvents()
    {
        var tracker = new DeviceTracker(); var first = tracker.Update([Device()], DateTimeOffset.Now).Single();
        var next = tracker.Update([Device()], DateTimeOffset.Now).Single();
        Assert.Equal(first.Target, next.Target); Assert.Equal(0, tracker.Events.Count);
    }
    [TestMethod] public void ReconnectWithReusedInstanceRefusesOldTarget()
    {
        var tracker = new DeviceTracker(); var original = tracker.Update([Device()], DateTimeOffset.Now).Single();
        tracker.Update([], DateTimeOffset.Now); var reconnected = tracker.Update([Device()], DateTimeOffset.Now).Single();
        Assert.NotEqual(original.Target, reconnected.Target);
        Assert.Throws<ObservationExpiredException>(() => tracker.Validate(original.Target!));
        Assert.Equal(reconnected, tracker.Validate(reconnected.Target!));
        Assert.Equal(2, tracker.Events.Count);
    }
    [TestMethod] public void NativeRemovalBetweenInventoriesEndsObservationEvenIfDeviceIsPresentAgain()
    {
        var tracker = new DeviceTracker(); var original = tracker.Update([Device()], DateTimeOffset.Now).Single();
        tracker.ObserveRemoval(original.InstanceId); Assert.Throws<ObservationExpiredException>(() => tracker.Validate(original.Target!));
        var fresh = tracker.Update([Device()], DateTimeOffset.Now).Single();
        Assert.NotEqual(original.Target, fresh.Target); Assert.Throws<ObservationExpiredException>(() => tracker.Validate(original.Target!));
    }
    [TestMethod] public void ChangedHardwareFingerprintCannotReuseSelectionIdentity()
    {
        var tracker = new DeviceTracker(); var original = tracker.Update([Device()], DateTimeOffset.Now).Single();
        var next = tracker.Update([Device() with { ContainerId = "replacement" }], DateTimeOffset.Now).Single();
        Assert.NotEqual(original.Target, next.Target); Assert.Throws<ObservationExpiredException>(() => tracker.Validate(original.Target!));
    }
    [TestMethod] public void DriverAndProblemChangesKeepIdentityAndReportChange()
    {
        var tracker = new DeviceTracker(); var original = tracker.Update([Device()], DateTimeOffset.Now).Single();
        var next = tracker.Update([Device() with { DriverKey = "newdriver", ProblemCode = 28 }], DateTimeOffset.Now).Single();
        Assert.Equal(original.Target, next.Target); Assert.Contains("28", next.State);
        Assert.Equal("Changed", tracker.Events.Single().Kind);
    }
    [TestMethod] public void UsbAncestryIncludesFunctionsButRejectsUnrelatedHardwareAndCycles()
    {
        var tracker = new DeviceTracker();
        var result = tracker.Update([Device("USB\\root"), Device("HID\\child", "USB\\root", "HID"), Device("ROOT\\other", "", "ROOT"), Device("ROOT\\cycleA", "ROOT\\cycleB", "ROOT"), Device("ROOT\\cycleB", "ROOT\\cycleA", "ROOT")], DateTimeOffset.Now);
        Assert.True(result.Single(d => d.InstanceId == "HID\\child").UsbRelated);
        Assert.False(result.Single(d => d.InstanceId == "ROOT\\other").UsbRelated);
        Assert.False(result.Single(d => d.InstanceId == "ROOT\\cycleA").UsbRelated);
    }
    [TestMethod] public void VendorProductIdsRequireStructuredHardwareIdAndNeverInventSerialOrSpeed()
    {
        Assert.Equal(("046D", "C534"), DeviceTracker.UsbIds(["USB\\vid_046d&pid_c534&rev_2900"]));
        Assert.Equal(("046D", "C534"), DeviceTracker.UsbIds(["HID\\VID_046D&PID_C534\\suffix"]));
        var invalid = DeviceTracker.UsbIds(["controller USB 3.2", "NOTVID_046D&PID_C534", "USB\\VID_046X&PID_C534"]);
        Assert.Null(invalid.Vendor); Assert.Null(invalid.Product);
    }
    [TestMethod] public void EventHistoryIsBoundedAndRowsUseStableTargetKeys()
    {
        var tracker = new DeviceTracker(3); tracker.Update([], DateTimeOffset.Now);
        for (var i = 0; i < 10; i++) tracker.Update([Device("USB\\" + i)], DateTimeOffset.Now);
        Assert.Equal(3, tracker.Events.Count);
        var devices = tracker.Update([Device("USB\\9")], DateTimeOffset.Now);
        var table = DeviceTables.Inventory(new(devices, tracker.Events, "fixture", 1, TimeSpan.Zero), "USB");
        Assert.Equal(devices.Single().Target!.Identity, table.Data!.Single().Key);
        Assert.True(table.Data!.Single().Subject is DeviceTarget);
    }
    [TestMethod] public void UsbAndProblemViewsRetainTypedFilteringFields()
    {
        var tracker = new DeviceTracker(); var devices = tracker.Update([Device(), Device("ROOT\\problem", "", "ROOT") with { ProblemCode = 10, HardwareIds = ["ROOT\\fixture"] }], DateTimeOffset.Now);
        var snapshot = new DeviceSnapshot(devices, [], "fixture", 1, TimeSpan.Zero);
        Assert.Equal(1, DeviceTables.Inventory(snapshot, "USB").Rows.Count);
        Assert.Equal("Problem 10", DeviceTables.Inventory(snapshot, "Problems").Rows.Single()[2]);
        var rows = DeviceTables.Inventory(snapshot, "All devices").Data!;
        Assert.Equal(1, TableRows.FilterSort(rows, "vendor:046d", [], default).Count);
    }
}
