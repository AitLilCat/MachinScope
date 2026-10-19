using Core.Domain.Models;
namespace Core.Application.Services;

public enum AdministrationGroup { WindowsAdministration, NetworkAdministration, StorageUtilities, SecurityUtilities, AdvancedConfiguration }
public enum EntryPointKind { Executable, ManagementConsole, ControlPanel, SettingsUri }
public enum FeatureAvailability { Available, RequiresElevation, UnsupportedPlatform, NotImplemented, TemporarilyUnavailable }
public sealed record NativeEntryPoint(EntryPointKind Kind, string Target, string? Argument = null, bool EditionRestricted = false);
public sealed record AdministrationTool(string Id, string Name, string Description, AdministrationGroup Group,
    PlatformKind? Platform, ToolPrivilege Privilege, string? Capability, NavigationDestination? Destination,
    NativeEntryPoint? EntryPoint, bool Implemented = true);
public sealed record ToolAvailability(string Id, FeatureAvailability State, string Explanation, bool CanOpen);
public interface IAdministrationTools
{
    Task<IReadOnlyList<ToolAvailability>> InspectAsync(bool force, CancellationToken token);
    Task OpenAsync(string id, CancellationToken token);
}

public static class AdministrationCatalog
{
    private static AdministrationTool Native(string id, string name, string description, AdministrationGroup group,
        EntryPointKind kind, string target, ToolPrivilege privilege = ToolPrivilege.User, string? argument = null, bool edition = false)
        => new(id, name, description + " Opens the native Windows utility; changes are made there under Windows permissions.", group,
            PlatformKind.Windows, privilege, "windows.tools", null, new(kind, target, argument, edition));
    public static IReadOnlyList<AdministrationTool> Tools { get; } = Array.AsReadOnly(ToolRegistry.Tools.Where(t => t.Id != "disk.utility").Select(t =>
        new AdministrationTool(t.Id, t.Name, t.Requirements?.RecoveryGuidance ?? "Opens the existing MachinScope inspection page.",
            t.Purpose switch { ToolPurpose.Storage => AdministrationGroup.StorageUtilities, ToolPurpose.Network => AdministrationGroup.NetworkAdministration,
                _ => AdministrationGroup.AdvancedConfiguration }, t.Platform, t.Privilege, t.Capability, t.Destination, null, t.Implemented))
        .Concat(new AdministrationTool[] {
            Native("windows.computer", "Computer Management", "System administration consoles.", AdministrationGroup.WindowsAdministration, EntryPointKind.ManagementConsole, "compmgmt.msc", ToolPrivilege.Administrator),
            Native("windows.devices", "Device Manager", "Inspect devices and manage drivers.", AdministrationGroup.WindowsAdministration, EntryPointKind.ManagementConsole, "devmgmt.msc", ToolPrivilege.Administrator),
            Native("disk.utility", "Disk Management", "Inspect partitions and volumes; native actions may destroy data.", AdministrationGroup.StorageUtilities, EntryPointKind.ManagementConsole, "diskmgmt.msc", ToolPrivilege.Administrator),
            Native("windows.events", "Event Viewer", "Inspect Windows event logs, including Security where permitted.", AdministrationGroup.WindowsAdministration, EntryPointKind.ManagementConsole, "eventvwr.msc"),
            Native("windows.services", "Services", "Inspect service state and configure services where permitted.", AdministrationGroup.WindowsAdministration, EntryPointKind.ManagementConsole, "services.msc", ToolPrivilege.Administrator),
            Native("windows.tasks", "Task Scheduler", "Inspect scheduled tasks and manage task definitions.", AdministrationGroup.WindowsAdministration, EntryPointKind.ManagementConsole, "taskschd.msc", ToolPrivilege.Administrator),
            Native("windows.information", "System Information", "Native hardware and software inventory.", AdministrationGroup.WindowsAdministration, EntryPointKind.Executable, "msinfo32.exe"),
            Native("windows.resources", "Resource Monitor", "Native resource and activity diagnostics.", AdministrationGroup.WindowsAdministration, EntryPointKind.Executable, "resmon.exe"),
            Native("windows.performance", "Performance Monitor", "Windows counters and performance analysis.", AdministrationGroup.WindowsAdministration, EntryPointKind.ManagementConsole, "perfmon.msc"),
            Native("windows.settings", "Windows Settings", "Windows configuration homepage.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.SettingsUri, "ms-settings:"),
            Native("windows.connections", "Network Connections", "Windows network adapter configuration.", AdministrationGroup.NetworkAdministration, EntryPointKind.ControlPanel, "ncpa.cpl", ToolPrivilege.Administrator),
            Native("windows.network", "Network adapter settings", "Windows network settings and status.", AdministrationGroup.NetworkAdministration, EntryPointKind.SettingsUri, "ms-settings:network-status"),
            Native("windows.troubleshoot", "Network troubleshooting", "Open Windows troubleshoot settings; available troubleshooters vary by build.", AdministrationGroup.NetworkAdministration, EntryPointKind.SettingsUri, "ms-settings:troubleshoot"),
            Native("windows.firewall", "Windows Firewall", "Native firewall configuration.", AdministrationGroup.SecurityUtilities, EntryPointKind.ControlPanel, "firewall.cpl", ToolPrivilege.Administrator),
            Native("windows.firewall.advanced", "Advanced firewall configuration", "Native rules, profiles and connection security.", AdministrationGroup.SecurityUtilities, EntryPointKind.ManagementConsole, "wf.msc", ToolPrivilege.Administrator),
            Native("windows.storage", "Storage configuration", "Windows storage settings.", AdministrationGroup.StorageUtilities, EntryPointKind.SettingsUri, "ms-settings:storagesense"),
            Native("windows.optimize", "Optimize Drives", "Windows disk optimization utility.", AdministrationGroup.StorageUtilities, EntryPointKind.Executable, "dfrgui.exe", ToolPrivilege.Administrator),
            Native("windows.cleanup", "Disk Cleanup", "Review cleanup candidates in the native utility before deleting.", AdministrationGroup.StorageUtilities, EntryPointKind.Executable, "cleanmgr.exe"),
            Native("windows.recovery", "Recovery settings", "Windows recovery options; review impact in Windows before acting.", AdministrationGroup.StorageUtilities, EntryPointKind.SettingsUri, "ms-settings:recovery", ToolPrivilege.Administrator),
            Native("windows.protection", "System Protection", "Windows restore points and system protection.", AdministrationGroup.StorageUtilities, EntryPointKind.Executable, "SystemPropertiesProtection.exe", ToolPrivilege.Administrator),
            Native("windows.accounts", "User accounts", "Native Windows account configuration.", AdministrationGroup.SecurityUtilities, EntryPointKind.Executable, "netplwiz.exe", ToolPrivilege.Administrator),
            Native("windows.localusers", "Local Users and Groups", "Local account management on supported Windows editions.", AdministrationGroup.SecurityUtilities, EntryPointKind.ManagementConsole, "lusrmgr.msc", ToolPrivilege.Administrator, edition: true),
            Native("windows.uac", "User Account Control settings", "Review UAC configuration in Windows.", AdministrationGroup.SecurityUtilities, EntryPointKind.Executable, "UserAccountControlSettings.exe", ToolPrivilege.Administrator),
            Native("windows.security", "Windows Security", "Open the registered Windows Security application.", AdministrationGroup.SecurityUtilities, EntryPointKind.SettingsUri, "windowsdefender:"),
            Native("windows.environment", "Environment variables / advanced system properties", "Advanced system properties includes environment-variable configuration.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.Executable, "SystemPropertiesAdvanced.exe"),
            Native("windows.startup", "Windows startup configuration", "Windows startup application settings.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.SettingsUri, "ms-settings:startupapps"),
            Native("windows.features", "Windows optional features", "Review optional operating-system components.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.Executable, "OptionalFeatures.exe", ToolPrivilege.Administrator),
            Native("windows.applications", "Installed applications", "Review applications in Windows Settings.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.SettingsUri, "ms-settings:appsfeatures"),
            Native("windows.update", "Windows Update", "Review Windows Update configuration.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.SettingsUri, "ms-settings:windowsupdate"),
            Native("windows.power", "Power management", "Windows power plans and configuration.", AdministrationGroup.AdvancedConfiguration, EntryPointKind.ControlPanel, "powercfg.cpl")
        }).ToArray());
    private static readonly IReadOnlyDictionary<string, string> SearchText = Tools.ToDictionary(t => t.Id, t => $"{t.Name} {t.Description} {GroupName(t.Group)} {t.Id}", StringComparer.Ordinal);
    public static string GroupName(AdministrationGroup group) => group switch {
        AdministrationGroup.WindowsAdministration => "Windows Administration", AdministrationGroup.NetworkAdministration => "Network Administration",
        AdministrationGroup.StorageUtilities => "Storage Utilities", AdministrationGroup.SecurityUtilities => "Security Utilities", _ => "Advanced Configuration" };
    public static IReadOnlyList<AdministrationTool> Filter(PlatformKind platform, string query = "", AdministrationGroup? group = null)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Tools.Where(t => (t.Platform == null || t.Platform == platform) && (group == null || t.Group == group) &&
            words.All(w => SearchText[t.Id].Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    public static ToolAvailability Availability(AdministrationTool tool, PlatformKind platform, IEnumerable<Capability> capabilities, bool elevated, bool nativeAvailable, string diagnostic)
    {
        if (tool.Platform is { } required && required != platform) return new(tool.Id, FeatureAvailability.UnsupportedPlatform, $"Requires {required}.", false);
        if (!tool.Implemented) return new(tool.Id, FeatureAvailability.NotImplemented, "Not implemented; no action provider is installed.", false);
        if (tool.Capability is { } id && CapabilityRegistry.Decision(id, capabilities) is { Allowed: false } denied)
            return new(tool.Id, FeatureAvailability.TemporarilyUnavailable, denied.Reason, false);
        if (tool.EntryPoint != null && !nativeAvailable) return new(tool.Id, FeatureAvailability.TemporarilyUnavailable, diagnostic, false);
        if (tool.Privilege == ToolPrivilege.Administrator && !elevated)
            return new(tool.Id, FeatureAvailability.RequiresElevation, "Administrator approval may be required for changes. Windows handles authorization in the external utility.", tool.EntryPoint != null);
        return new(tool.Id, FeatureAvailability.Available, diagnostic, true);
    }
}
