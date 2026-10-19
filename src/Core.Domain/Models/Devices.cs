namespace Core.Domain.Models;

public sealed record DeviceTarget(string InstanceId, string Fingerprint, string ObservationId)
{
    public string Identity => $"{InstanceId}|{Fingerprint}|{ObservationId}";
}
public sealed record DeviceRecord(string InstanceId, string Name, string Manufacturer, string Class,
    string ClassGuid, string Enumerator, string Service, string DriverKey, string ParentId,
    string Location, string[] HardwareIds, string[] CompatibleIds, string ContainerId,
    uint? StatusFlags, uint? ProblemCode, bool? Removable)
{
    public DeviceTarget? Target { get; init; }
    public bool UsbRelated { get; init; }
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }
    // An instance suffix is not necessarily a device-provided serial number.
    public string Fingerprint => $"{ClassGuid}|{ContainerId}|{string.Join(';', HardwareIds)}";
    public string State => ProblemCode is > 0 ? $"Problem {ProblemCode}" : StatusFlags == null ? "Status unavailable" : "Present";
}
public sealed record DeviceEvent(long Sequence, DateTimeOffset Time, string Kind, string InstanceId, string Name, string Detail);
public sealed record DeviceSnapshot(IReadOnlyList<DeviceRecord> Devices, IReadOnlyList<DeviceEvent> Events,
    string NotificationStatus, long EnumerationCount, TimeSpan EnumerationTime);
public sealed record DeviceDetails(DeviceTarget Target, string Name, IReadOnlyList<InspectionTable> Tabs);
