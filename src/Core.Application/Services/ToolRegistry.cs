using Core.Domain.Models;
namespace Core.Application.Services;

public enum ToolLevel { Inspect, Configure, Advanced }
public enum ToolPurpose { Storage, Network, Devices, Filesystem, OperatingSystem }
public enum ToolCommand { None, DiskManagement }
public enum ToolPrivilege { User, Administrator }
public sealed record MutationRequirements(bool ExactIdentity, bool InspectCurrentState, bool Preview, bool VerifyPrivilege, bool Confirm, bool VerifyResult, bool StructuredResults, string RecoveryGuidance);
public sealed record SystemTool(string Id, string Name, ToolLevel Level, PlatformKind? Platform, ToolPrivilege Privilege, bool Mutating, ActionRisk Risk, string[] Actions, string? Capability, NavigationDestination? Destination, bool Implemented, MutationRequirements? Requirements = null) { public ToolPurpose Purpose { get; init; } = ToolPurpose.OperatingSystem; public ToolCommand Command { get; init; } }
public static class ToolRegistry
{
    // Metadata is intentionally not an execution API. Implementations must keep their native handle/identity checks.
    public static IReadOnlyList<SystemTool> Tools { get; } = Array.AsReadOnly(new SystemTool[] {
        new("platform.inspect", "Platform, permissions and capabilities", ToolLevel.Inspect, null, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "platform.inspect", NavigationDestination.Security, true),
        new("os.inspect", "Operating system inventory", ToolLevel.Inspect, null, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "os.inspect", NavigationDestination.OperatingSystem, true),
        new("disk.health", "Disk reliability and diagnostics", ToolLevel.Inspect, PlatformKind.Windows, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "disk.inspect", NavigationDestination.DiskHealth, true) { Purpose = ToolPurpose.Storage },
        new("disk.utility", "Windows Disk Management", ToolLevel.Advanced, PlatformKind.Windows, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Open external utility"], "disk.inspect", null, true) { Purpose = ToolPurpose.Storage, Command = ToolCommand.DiskManagement },
        new("network.diagnostics", "Network diagnostics", ToolLevel.Inspect, null, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "network.diagnostics", NavigationDestination.Diagnostics, true) { Purpose = ToolPurpose.Network },
        new("devices.inspect", "Device relationships and drivers", ToolLevel.Inspect, null, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "devices.inspect", NavigationDestination.Devices, true) { Purpose = ToolPurpose.Devices },
        new("files.analysis", "File analysis and duplicates", ToolLevel.Inspect, null, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "files.inspect", NavigationDestination.Files, true) { Purpose = ToolPurpose.Filesystem },
        new("startup.inspect", "Startup inventory", ToolLevel.Inspect, PlatformKind.Windows, ToolPrivilege.User, false, ActionRisk.ReadOnly, ["Inspect"], "startup.inspect", NavigationDestination.Startup, true),
        new("startup.configure", "Current-user startup entries", ToolLevel.Configure, PlatformKind.Windows, ToolPrivilege.User, true, ActionRisk.LowRisk, ["Disable", "Restore"], "startup.control", NavigationDestination.Startup, true,
            new(true, true, true, true, true, true, true, "Disable retains a recovery backup. Restore revalidates the entry; conflicts require manual review.")),
        new("policies.configure", "System policy configuration", ToolLevel.Configure, PlatformKind.Windows, ToolPrivilege.Administrator, true, ActionRisk.Elevated, ["Configure"], null, null, false,
            new(true, true, true, true, true, true, true, "Unavailable. A provider must supply recovery guidance; rollback is not currently implemented.")),
        new("disk.advanced", "Native disk modification", ToolLevel.Advanced, PlatformKind.Windows, ToolPrivilege.Administrator, true, ActionRisk.Destructive | ActionRisk.Elevated, ["Modify"], "disk.mutate", null, false,
            new(true, true, true, true, true, true, true, "Unavailable. Destructive changes may be irreversible; no rollback is provided.")) { Purpose = ToolPurpose.Storage } });
    public static ActionDecision Availability(SystemTool tool, PlatformKind platform, IEnumerable<Capability> capabilities)
    {
        if (!tool.Implemented) return ActionDecision.Deny("Planned; no action provider is installed.");
        if (tool.Platform is { } required && platform != required) return ActionDecision.Deny($"Requires {required}.");
        if (tool.Capability is { } id) return CapabilityRegistry.Decision(id, capabilities);
        return ActionDecision.Permit;
    }
}
