using System.Text.RegularExpressions;
using Core.Domain.Models;
namespace Core.Application.Services;

/// <summary>Caller serializes access. Identities distinguish observed unplug/replug cycles.</summary>
public sealed class DeviceTracker(int capacity = 512)
{
    private Dictionary<string, DeviceRecord> current = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DeviceEvent> events = new();
    private readonly HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase);
    private long sequence;
    private bool initialized;
    public IReadOnlyList<DeviceEvent> Events => events.ToArray();
    public void ObserveRemoval(string instanceId) { if (instanceId.Length > 0) removed.Add(instanceId); }
    private void Event(DateTimeOffset time, string kind, DeviceRecord device, string detail)
    {
        events.Enqueue(new(++sequence, time, kind, device.InstanceId, device.Name, detail));
        while (events.Count > Math.Max(1, capacity)) events.Dequeue();
    }
    public IReadOnlyList<DeviceRecord> Update(IReadOnlyList<DeviceRecord> devices, DateTimeOffset time)
    {
        var next = new Dictionary<string, DeviceRecord>(StringComparer.OrdinalIgnoreCase);
        var raw = devices.ToDictionary(d => d.InstanceId, StringComparer.OrdinalIgnoreCase);
        foreach (var item in devices)
        {
            current.TryGetValue(item.InstanceId, out var old);
            var same = old != null && old.Fingerprint == item.Fingerprint && !removed.Contains(item.InstanceId);
            var ids = UsbIds(item.HardwareIds.Append(item.InstanceId));
            var record = item with { Target = same ? old!.Target : new(item.InstanceId, item.Fingerprint, Guid.NewGuid().ToString("N")),
                UsbRelated = IsUsb(item, raw), VendorId = ids.Vendor, ProductId = ids.Product };
            next.Add(record.InstanceId, record);
            if (initialized && !same)
            {
                if (old != null) Event(time, "Removed / replaced", old, "Prior observation ended; instance identifiers can be reused");
                Event(time, "Connected", record, "Observed present in SetupAPI inventory");
            }
            else if (same && old != null && (old.State != record.State || old.DriverKey != record.DriverKey || old.Service != record.Service || old.Name != record.Name))
                Event(time, "Changed", record, $"{old.State} → {record.State}; driver/service/description may have changed");
        }
        if (initialized) foreach (var old in current.Values.Where(d => !next.ContainsKey(d.InstanceId))) Event(time, "Disconnected", old, "No longer present in SetupAPI inventory");
        current = next; initialized = true; removed.Clear();
        return current.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.InstanceId).ToArray();
    }
    public DeviceRecord Validate(DeviceTarget target)
    {
        if (!current.TryGetValue(target.InstanceId, out var record) || record.Target?.Identity != target.Identity || removed.Contains(target.InstanceId))
            throw new ObservationExpiredException("Device disappeared, changed identity or reconnected. Refresh and select it again.");
        return record;
    }
    public static (string? Vendor, string? Product) UsbIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            var match = Regex.Match(id, @"(?:^|[\\&])VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?:[\\&]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) return (match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value.ToUpperInvariant());
        }
        return (null, null);
    }
    private static bool IsUsb(DeviceRecord item, IReadOnlyDictionary<string, DeviceRecord> all)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (seen.Add(item.InstanceId))
        {
            if (item.Enumerator.Equals("USB", StringComparison.OrdinalIgnoreCase) || item.Enumerator.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) || item.Class.Equals("USB", StringComparison.OrdinalIgnoreCase)) return true;
            if (!all.TryGetValue(item.ParentId, out var parent)) break;
            item = parent;
        }
        return false;
    }
}
