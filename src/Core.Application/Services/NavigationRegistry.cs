using Core.Domain.Models;
namespace Core.Application.Services;

public enum NavigationCategory { Activity, Hardware, Network, Configuration, Advanced }
public enum NavigationRefresh { SummaryOnly, ActivitySummary, Processes, Performance, Network, Devices }
public enum NavigationDestination { CoreOverview, ActivityOverview, Processes, Performance, Startup, ResourceMonitor, HardwareOverview, DisksVolumes, DiskHealth, Files, Devices, DiskImages, NetworkOverview, Connections, Adapters, Traffic, Diagnostics, ConfigurationOverview, OperatingSystem, Security, Maintenance, Policies, Tools, Preferences, PhysicalDisks = DisksVolumes, Volumes = DisksVolumes, Services = Startup, OSSettings = OperatingSystem }
public sealed record NavigationRoute(NavigationDestination Destination, string Id, string Name, NavigationCategory? Category, string LegacyPage, bool Homepage = false, PlatformKind? Platform = null, string? Capability = null, bool Implemented = true, string? View = null, bool Visible = true)
{
    public NavigationRefresh Refresh => Destination == NavigationDestination.ActivityOverview ? NavigationRefresh.ActivitySummary : LegacyPage switch { "Processes" => NavigationRefresh.Processes, "Performance" => NavigationRefresh.Performance, "Network" => NavigationRefresh.Network, "Devices" => NavigationRefresh.Devices, _ => NavigationRefresh.SummaryOnly };
}
public sealed record NavigationGroup(NavigationCategory Category, string Id, string Name, NavigationDestination Homepage);

// Registration contains no collectors, UI objects or actions. Availability is evaluated against the installed providers.
public static class NavigationRegistry
{
    static NavigationRegistry() => Validate(Routes, Groups);
    public static void Validate(IEnumerable<NavigationRoute> routes, IEnumerable<NavigationGroup> groups)
    {
        var pages = routes.ToArray(); var categories = groups.ToArray();
        if (pages.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != pages.Length || pages.Select(r => r.Destination).Distinct().Count() != pages.Length)
            throw new ArgumentException("Duplicate navigation destination or stable ID");
        if (categories.Select(g => g.Category).Distinct().Count() != categories.Length || categories.Select(g => g.Id).Distinct(StringComparer.Ordinal).Count() != categories.Length)
            throw new ArgumentException("Duplicate navigation category");
        if (pages.Any(r => r.Category is { } category && categories.All(g => g.Category != category))) throw new ArgumentException("Orphaned navigation route");
        foreach (var group in categories) if (!pages.Any(r => r.Destination == group.Homepage && r.Homepage && r.Category == group.Category)) throw new ArgumentException("Missing category homepage");
    }
    public static IReadOnlyList<NavigationGroup> Groups { get; } = Array.AsReadOnly(new NavigationGroup[] {
        new(NavigationCategory.Activity, "activity", "System Activity", NavigationDestination.ActivityOverview),
        new(NavigationCategory.Hardware, "hardware", "Hardware & Storage", NavigationDestination.HardwareOverview),
        new(NavigationCategory.Network, "network", "Network & Connectivity", NavigationDestination.NetworkOverview),
        new(NavigationCategory.Configuration, "configuration", "System Configuration", NavigationDestination.ConfigurationOverview),
        new(NavigationCategory.Advanced, "advanced", "Advanced Tools", NavigationDestination.Tools) });
    public static IReadOnlyList<NavigationRoute> Routes { get; } = Array.AsReadOnly(new NavigationRoute[] {
        new(NavigationDestination.CoreOverview, "core.overview", "MachinScope Overview", null, "Category", true),
        new(NavigationDestination.ActivityOverview, "activity.overview", "Overview", NavigationCategory.Activity, "Category", true),
        new(NavigationDestination.Processes, "activity.processes", "Processes", NavigationCategory.Activity, "Processes", Capability: "process.inspect"),
        new(NavigationDestination.Performance, "activity.performance", "Performance", NavigationCategory.Activity, "Performance", Capability: "performance.inspect"),
        new(NavigationDestination.Startup, "activity.startup", "Startup & Services", NavigationCategory.Activity, "Startup", Platform: PlatformKind.Windows, Capability: "startup.inspect"),
        new(NavigationDestination.ResourceMonitor, "activity.resources", "Resource Monitor", NavigationCategory.Activity, "", Implemented: false, Visible: false),
        new(NavigationDestination.HardwareOverview, "hardware.overview", "Overview", NavigationCategory.Hardware, "Category", true),
        new(NavigationDestination.DisksVolumes, "hardware.storage", "Disks & Volumes", NavigationCategory.Hardware, "Storage", Capability: "disk.inspect"),
        new(NavigationDestination.DiskHealth, "hardware.health", "Disk Health", NavigationCategory.Hardware, "Health", Capability: "disk.inspect"),
        new(NavigationDestination.Files, "hardware.files", "Files & Cleanup", NavigationCategory.Hardware, "Files", Capability: "files.inspect"),
        new(NavigationDestination.Devices, "hardware.devices", "USB & Devices", NavigationCategory.Hardware, "Devices", Capability: "devices.inspect"),
        new(NavigationDestination.DiskImages, "hardware.images", "Disk Images & ISO", NavigationCategory.Hardware, "", Capability: "images.inspect", Implemented: false),
        new(NavigationDestination.NetworkOverview, "network.overview", "Overview", NavigationCategory.Network, "Category", true),
        new(NavigationDestination.Connections, "network.connections", "Connections", NavigationCategory.Network, "Network", Capability: "network.inspect", View: "Connections"),
        new(NavigationDestination.Adapters, "network.adapters", "Adapters", NavigationCategory.Network, "Network", Capability: "network.inspect", View: "Interfaces"),
        new(NavigationDestination.Traffic, "network.traffic", "Traffic Inspector", NavigationCategory.Network, "Network", Capability: "network.inspect", View: "Traffic"),
        new(NavigationDestination.Diagnostics, "network.diagnostics", "Diagnostics", NavigationCategory.Network, "Category", true, Capability: "network.diagnostics"),
        new(NavigationDestination.ConfigurationOverview, "configuration.overview", "Overview", NavigationCategory.Configuration, "Category", true),
        new(NavigationDestination.OperatingSystem, "configuration.os", "Operating System", NavigationCategory.Configuration, "Overview", Capability: "os.inspect"),
        new(NavigationDestination.Security, "configuration.security", "Security & Permissions", NavigationCategory.Configuration, "Security", Capability: "platform.inspect"),
        new(NavigationDestination.Maintenance, "configuration.maintenance", "Maintenance", NavigationCategory.Configuration, "Category", true),
        new(NavigationDestination.Policies, "configuration.policies", "System Policies", NavigationCategory.Configuration, "", Implemented: false, Visible: false),
        new(NavigationDestination.Tools, "advanced.overview", "Advanced Tools", NavigationCategory.Advanced, "Category", true),
        new(NavigationDestination.Preferences, "core.preferences", "MachinScope Preferences", null, "Settings") });
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string> {
        ["hardware.disks"] = "hardware.storage", ["hardware.volumes"] = "hardware.storage",
        ["configuration.startup"] = "activity.startup", ["configuration.tools"] = "advanced.overview",
        ["configuration.settings"] = "configuration.os" };
    public static NavigationRoute? Resolve(string? id) => Routes.FirstOrDefault(r => r.Id == (id != null && Aliases.TryGetValue(id, out var canonical) ? canonical : id));
    public static NavigationRoute Get(NavigationDestination destination) => Routes.Single(r => r.Destination == destination);
    public static ActionDecision Availability(NavigationRoute route, PlatformKind platform, IEnumerable<Capability> capabilities)
    {
        if (!route.Implemented) return ActionDecision.Deny("Planned capability; no provider is implemented.");
        if (route.Platform is { } required && platform != required) return ActionDecision.Deny($"Requires {required} providers.");
        if (route.Capability is { } id) return CapabilityRegistry.Decision(id, capabilities);
        return ActionDecision.Permit;
    }
}

public sealed class NavigationCoordinator
{
    private readonly PlatformKind platform;
    private readonly Capability[] capabilities;
    private readonly HashSet<NavigationCategory> expanded = [];
    public NavigationDestination Selected { get; private set; } = NavigationDestination.CoreOverview;
    private readonly List<NavigationDestination> history = [];
    private int historyIndex = -1;
    public bool CanBack => historyIndex > 0;
    public bool CanForward => historyIndex >= 0 && historyIndex < history.Count - 1;
    public bool Back() => Move(-1);
    public bool Forward() => Move(1);
    private bool Move(int direction) { var next = historyIndex + direction; if (next < 0 || next >= history.Count) return false; historyIndex = next; Selected = history[next]; RevealParent(); return true; }
    private void RevealParent() { var route = NavigationRegistry.Get(Selected); if (!route.Homepage && route.Category is { } category) expanded.Add(category); }
    public bool PaneOpen { get; set; } = true;
    public bool IsExpanded(NavigationCategory category) => expanded.Contains(category);
    public NavigationCoordinator(PlatformKind platform, IEnumerable<Capability> capabilities, NavigationPreferences? saved = null)
    {
        this.platform = platform; this.capabilities = capabilities.ToArray();
        if (saved == null) { history.Add(Selected); historyIndex = 0; return; }
        PaneOpen = saved.PaneOpen;
        foreach (var group in NavigationRegistry.Groups) if ((saved.ExpandedCategories ?? []).Contains(group.Id)) expanded.Add(group.Category);
        var restored = NavigationRegistry.Resolve(saved.LastDestination);
        if (restored != null && NavigationRegistry.Availability(restored, platform, this.capabilities).Allowed) Selected = restored.Destination;
        RevealParent(); history.Add(Selected); historyIndex = 0;
    }
    public bool Select(NavigationDestination destination, bool expandCategory = true)
    {
        var route = NavigationRegistry.Get(destination);
        if (!NavigationRegistry.Availability(route, platform, capabilities).Allowed) return false;
        if (Selected != destination) { if (historyIndex < history.Count - 1) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1); history.Add(destination); if (history.Count > 100) history.RemoveAt(0); historyIndex = history.Count - 1; }
        Selected = destination;
        if (expandCategory && !route.Homepage && route.Category is { } category) SetExpanded(category, true);
        return true;
    }
    public void SetExpanded(NavigationCategory category, bool value) { if (value) expanded.Add(category); else expanded.Remove(category); }
    public NavigationPreferences Snapshot() => new(NavigationRegistry.Get(Selected).Id, NavigationRegistry.Groups.Where(g => expanded.Contains(g.Category)).Select(g => g.Id).ToArray(), PaneOpen);
}
