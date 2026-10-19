namespace Core.Domain.Models;

public enum DiskProtection { Protected, Unknown, DataCandidate }
public sealed record VolumeTarget(string Path, string DeviceId, string Label, string FileSystem, ulong? Capacity, ulong? Free, bool? System, bool? Boot, bool PageFile);
public sealed record PartitionTarget(int DiskNumber, int Number, string DeviceId, string Type, ulong? Offset, ulong? Capacity, bool Boot, IReadOnlyList<VolumeTarget> Volumes);
public sealed record DiskTarget(int Number, string DevicePath, string Model, string Serial, ulong Capacity, string? UniqueId)
{
    public string Identity => $"{DevicePath}|{UniqueId}|{Serial}|{Capacity}";
}
public sealed record InspectedDisk(DiskTarget Target, string Firmware, string Bus, string ProviderStatus, IReadOnlyList<PartitionTarget> Partitions, DiskProtection Protection, IReadOnlyList<string> ProtectionReasons, IReadOnlyDictionary<string, string> Raw);
public sealed record DiskInventory(IReadOnlyList<InspectedDisk> Disks, IReadOnlyList<VolumeTarget> Volumes, IReadOnlyList<string> Issues);
public sealed record DiskDetails(DiskTarget Target, IReadOnlyList<InspectionTable> Tabs);
