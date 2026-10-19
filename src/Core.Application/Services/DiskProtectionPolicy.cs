using Core.Domain.Models;
namespace Core.Application.Services;

public static class DiskProtectionPolicy
{
    public static (DiskProtection State, string[] Reasons) Classify(bool? isSystem, bool? isBoot, IReadOnlyList<PartitionTarget> partitions, bool completeMapping)
    {
        var reasons = new List<string>();
        if (isSystem == true) reasons.Add("Contains the running operating system");
        if (isBoot == true) reasons.Add("Contains boot infrastructure");
        foreach (var partition in partitions)
        {
            if (partition.Boot || partition.Type.Contains("EFI", StringComparison.OrdinalIgnoreCase) || partition.Type.Contains("Recovery", StringComparison.OrdinalIgnoreCase)) reasons.Add($"Protected partition: {partition.DeviceId}");
            foreach (var volume in partition.Volumes) if (volume.System == true || volume.Boot == true || volume.PageFile) reasons.Add($"System, boot or pagefile volume: {volume.Path}");
        }
        if (reasons.Count > 0) return (DiskProtection.Protected, reasons.Distinct().ToArray());
        if (!completeMapping || isSystem == null || isBoot == null) return (DiskProtection.Unknown, ["System/boot classification or volume mapping is incomplete; management is blocked"]);
        return (DiskProtection.DataCandidate, ["No system role found in this snapshot; this is not permission to modify the disk"]);
    }
    public static ActionDecision ManagementDecision(InspectedDisk disk) => ActionDecision.Deny(disk.Protection == DiskProtection.Protected ? string.Join("; ", disk.ProtectionReasons) : "Disk mutations are unavailable until a native backend, dependency validation and isolated virtual-disk tests are implemented");
}
