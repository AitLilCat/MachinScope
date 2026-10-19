using System.Diagnostics;
using System.Management;
using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
namespace Core.Windows.Devices;

public sealed class WindowsDeviceInspector(IDiskInspector disks) : IUsbInspector, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object signalsLock = new();
    private readonly Queue<(uint Action, string Path, DateTimeOffset Time)> signals = new();
    private readonly Queue<DeviceEvent> nativeHistory = new();
    private readonly DeviceTracker tracker = new();
    private DeviceNotifications? notifications;
    private DeviceSnapshot? cached;
    private long revision, collectedRevision = -1, enumerationCount, nativeSequence;
    private long lastEnumeration;
    private bool disposed;
    private void Changed(uint action, string path)
    {
        lock (signalsLock) {
            signals.Enqueue((action, path, DateTimeOffset.Now));
            while (signals.Count > 256) signals.Dequeue();
            Interlocked.Increment(ref revision);
        }
    }
    public async Task<DeviceSnapshot> SnapshotAsync(bool force, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(() => Collect(force, token), token);
        } finally { gate.Release(); }
    }
    private DeviceSnapshot Collect(bool force, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); notifications ??= new DeviceNotifications(Changed);
        var currentRevision = Interlocked.Read(ref revision);
        if (!force && cached != null && currentRevision == collectedRevision && (notifications.Complete || Stopwatch.GetElapsedTime(lastEnumeration) < TimeSpan.FromSeconds(5))) return cached;
        // Register before enumerating so changes during collection remain dirty for the next pass.
        var clock = Stopwatch.StartNew(); var raw = DeviceNative.Enumerate(token);
        token.ThrowIfCancellationRequested();
        lock (signalsLock) {
            while (signals.TryDequeue(out var signal)) {
                if (signal.Action == 9) tracker.ObserveRemoval(signal.Path);
                var label = signal.Action switch { 0 => "Interface arrival", 1 => "Interface removal", 7 => "Instance enumerated", 8 => "Instance started", 9 => "Instance removed", _ => $"PnP event {signal.Action}" };
                nativeHistory.Enqueue(new(--nativeSequence, signal.Time, label, signal.Path, "Native notification", "Configuration Manager; interface events do not establish physical removal or mount state"));
                while (nativeHistory.Count > 256) nativeHistory.Dequeue();
            }
        }
        var devices = tracker.Update(raw, DateTimeOffset.Now);
        var events = tracker.Events.Concat(nativeHistory).OrderBy(e => e.Time).ToArray();
        lastEnumeration = Stopwatch.GetTimestamp(); collectedRevision = currentRevision;
        cached = new(devices, events, notifications.Status + (notifications.CallbackFailures > 0 ? $"; callback failures: {notifications.CallbackFailures}" : ""), ++enumerationCount, clock.Elapsed);
        return cached;
    }
    private DeviceRecord Validate(DeviceTarget target)
    {
        lock (signalsLock) {
            foreach (var signal in signals.Where(s => s.Action == 9)) tracker.ObserveRemoval(signal.Path);
        }
        return tracker.Validate(target);
    }
    public async Task<DeviceDetails> DetailsAsync(DeviceTarget target, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(async () => {
                var snapshot = Collect(true, token); var device = Validate(target);
                var properties = DeviceNative.DetailProperties(device.InstanceId);
                var tabs = new List<InspectionTable>();
                static string Value(object? value) => value == null || string.IsNullOrWhiteSpace(value.ToString()) ? "Unavailable" : value.ToString()!;
                static InspectionTable Pairs(string name, IEnumerable<(string Key, object? Value)> values, string? notice = null) => new(name, ["Property", "Value"], values.Select(p => new[] { p.Key, Value(p.Value) }).ToArray(), notice);
                tabs.Add(Pairs("General", [("Name", device.Name), ("Manufacturer", device.Manufacturer), ("State", device.State), ("Problem code", device.ProblemCode), ("Class", device.Class), ("Instance", device.InstanceId), ("Observed identity", target.Identity)]));
                tabs.Add(Pairs("Hardware", [("Vendor ID", device.VendorId), ("Product ID", device.ProductId), ("Hardware IDs", string.Join("\n", device.HardwareIds)), ("Compatible IDs", string.Join("\n", device.CompatibleIds)), ("Container ID", device.ContainerId), ("Description", properties["Bus-reported description"]), ("Serial number", null)], "Hardware IDs are reported verbatim. The instance suffix is not assumed to be a hardware serial. USB descriptor queries are pending."));
                tabs.Add(Pairs("Connection", [("Enumerator", device.Enumerator), ("USB ancestry", device.UsbRelated), ("Location", device.Location), ("Location paths", properties["Location paths"]), ("Removable capability", device.Removable), ("USB specification", null), ("Negotiated speed", null), ("Power characteristics", null)], "Removable capability is a device property, not permission to eject, format or write. Negotiated speed requires USB hub descriptor queries."));
                tabs.Add(Pairs("Driver", properties.Where(p => p.Key.StartsWith("Driver")).Select(p => (p.Key, (object?)p.Value)).Concat([("Service", device.Service), ("Driver key", device.DriverKey)]), "SetupAPI device properties; no driver changes are performed."));
                var relations = snapshot.Devices.Where(d => d.InstanceId.Equals(device.ParentId, StringComparison.OrdinalIgnoreCase) || d.ParentId.Equals(device.InstanceId, StringComparison.OrdinalIgnoreCase)).Select(d => {
                    string[] cells = [d.InstanceId.Equals(device.ParentId, StringComparison.OrdinalIgnoreCase) ? "Parent" : "Child", d.Name, d.Class, d.InstanceId];
                    return new InspectionRow(d.Target!.Identity, cells, cells, new Dictionary<string, object?> { ["name"] = d.Name, ["instance"] = d.InstanceId }, d.Target);
                }).ToArray();
                tabs.Add(new("Topology", ["Relation", "Name", "Class", "Instance"], relations.Select(r => r.Cells).ToArray(), "Native Configuration Manager parent relationships. This is not inferred physical cable topology.", relations));
                var relatedEvents = snapshot.Events.Where(e => e.InstanceId.Equals(device.InstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
                tabs.Add(DeviceTables.Events(snapshot with { Events = relatedEvents }) with { Title = "Events", Notice = "Only events with this exact device instance identifier. Initial inventory is a baseline, not a connect event." });
                if (device.UsbRelated || device.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)) tabs.Add(await Storage(device, snapshot, token));
                tabs.Add(Pairs("Advanced", [("Class GUID", device.ClassGuid), ("Container ID", device.ContainerId), ("Status flags", device.StatusFlags.HasValue ? $"0x{device.StatusFlags:X8}" : null), ("Notification provider", snapshot.NotificationStatus), ("Inventory count", snapshot.EnumerationCount)], "Read-only checkpoint. Disable, enable, safe eject, imaging and image writing are not implemented."));
                token.ThrowIfCancellationRequested(); Collect(true, token); Validate(target);
                return new DeviceDetails(target, device.Name, tabs);
            }, token);
        } finally { gate.Release(); }
    }
    private async Task<InspectionTable> Storage(DeviceRecord device, DeviceSnapshot snapshot, CancellationToken token)
    {
        var records = snapshot.Devices.ToDictionary(d => d.InstanceId, StringComparer.OrdinalIgnoreCase);
        bool Descendant(string id) {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (visited.Add(id)) { if (id.Equals(device.InstanceId, StringComparison.OrdinalIgnoreCase)) return true; if (!records.TryGetValue(id, out var record)) break; id = record.ParentId; }
            return false;
        }
        var rows = new List<InspectionRow>();
        try {
            var numbers = new HashSet<int>();
            using var query = new ManagementObjectSearcher("SELECT Index,PNPDeviceID FROM Win32_DiskDrive") { Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) } };
            using var result = query.Get();
            foreach (ManagementBaseObject item in result) using (item) { token.ThrowIfCancellationRequested(); if (Descendant(item["PNPDeviceID"]?.ToString() ?? "") && int.TryParse(item["Index"]?.ToString(), out var number)) numbers.Add(number); }
            if (numbers.Count > 0) {
                var inventory = await disks.InventoryAsync(token);
                foreach (var disk in inventory.Disks.Where(d => numbers.Contains(d.Target.Number))) {
                    string[] cells = ["Disk", disk.Target.Model, disk.Target.DevicePath, Calculations.Bytes(disk.Target.Capacity), disk.Protection.ToString()];
                    rows.Add(new(disk.Target.Identity, cells, cells, new Dictionary<string, object?>(), disk.Target));
                    foreach (var volume in disk.Partitions.SelectMany(p => p.Volumes).DistinctBy(v => v.DeviceId)) {
                        string[] data = ["Volume", volume.Label, volume.Path, ValueSize(volume.Capacity), volume.FileSystem];
                        rows.Add(new(volume.DeviceId, data, data, new Dictionary<string, object?>(), volume));
                    }
                }
            }
        } catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) {
            return new("Storage", ["Provider", "Status"], [new[] { "WMI storage association", e.Message }], "Unavailable; no storage capability is inferred.");
        }
        return new("Storage", ["Kind", "Name", "Path", "Capacity", "Protection / filesystem"], rows.Select(r => r.Cells).ToArray(), "Storage associations require exact disk PnP ID plus native ancestry. Empty results mean no mapped disk was found; this does not authorize raw writes.", rows);
    }
    private static string ValueSize(ulong? size) => size.HasValue ? Calculations.Bytes(size.Value) : "Unavailable";
    public void Dispose()
    {
        gate.Wait();
        try { if (disposed) return; disposed = true; notifications?.Dispose(); notifications = null; }
        finally { gate.Release(); }
    }
}
