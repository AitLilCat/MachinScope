using Core.Domain.Models;
namespace Core.Application.Services;

public static class DeviceTables
{
    public static InspectionTable Inventory(DeviceSnapshot snapshot, string view)
    {
        var source = snapshot.Devices.Where(d => view != "USB" || d.UsbRelated).Where(d => view != "Problems" || d.ProblemCode is > 0);
        var data = source.Select(d => {
            string[] cells = [d.Name, d.Class, d.State, d.Manufacturer, d.VendorId ?? "Unavailable", d.ProductId ?? "Unavailable", d.Service, d.Location, d.InstanceId, d.ParentId];
            return new InspectionRow(d.Target!.Identity, cells, cells, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                ["name"] = d.Name, ["class"] = d.Class, ["state"] = d.State, ["vendor"] = d.VendorId, ["product"] = d.ProductId,
                ["manufacturer"] = d.Manufacturer, ["instance"] = d.InstanceId, ["parent"] = d.ParentId, ["usb"] = d.UsbRelated, ["problem"] = d.ProblemCode }, d.Target);
        }).ToArray();
        return new("Devices", ["Name", "Class", "State", "Manufacturer", "Vendor ID", "Product ID", "Driver service", "Location", "Instance path", "Parent instance"],
            data.Select(d => d.Cells).ToArray(), $"{snapshot.NotificationStatus} · {snapshot.EnumerationCount} enumerations; last {snapshot.EnumerationTime.TotalMilliseconds:0} ms. USB view includes USB ancestry. IDs come from hardware identifiers; speed/serial are not inferred.", data);
    }
    public static InspectionTable Events(DeviceSnapshot snapshot)
    {
        var data = snapshot.Events.Reverse().Select(e => { string[] cells = [e.Time.ToString("O"), e.Kind, e.Name, e.InstanceId, e.Detail]; return new InspectionRow(e.Sequence.ToString(), cells, cells, new Dictionary<string, object?> { ["kind"] = e.Kind, ["instance"] = e.InstanceId, ["name"] = e.Name }); }).ToArray();
        return new("Device events", ["Time", "Kind", "Name", "Instance / interface", "Detail"], data.Select(r => r.Cells).ToArray(), "Bounded local session observations and native PnP notifications. Native interface events are not proof of a volume mount or physical device removal.", data);
    }
}
