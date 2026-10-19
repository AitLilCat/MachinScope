using Core.Domain.Models;
namespace Core.Application.Services;

public enum InspectionScope { General, Process, Connection, Adapter, Device, Disk, File }
public sealed record InspectionDescription(string Id, string Title, string ShortDescription, string? ExtendedExplanation = null, PlatformKind? Platform = null);
public static class InspectionDescriptions
{
    private static InspectionDescription Tab(string id, string title, string text, string? more = null, PlatformKind? platform = null) => new(id, title, text, more, platform);
    public static IReadOnlyList<InspectionDescription> Tabs { get; } = Array.AsReadOnly(new[] {
        Tab("general", "General", "Confirms the selected object's identity, state and primary properties. Unavailable fields describe limits of this inspection."),
        Tab("process.general", "General", "Identifies the selected process and its creation time, path and user where accessible. Check Inspection status to distinguish verified live data from the selected snapshot."),
        Tab("connection.endpoint", "Connection", "Shows the selected endpoint, protocol, state and observed identity. Use it to investigate application connections; an address does not establish remote ownership or intent.", "First observed is a MachinScope observation, not connection creation. Native bind timestamps are shown only when Windows exposes them. Snapshots cannot establish initiator direction or per-connection byte counts."),
        Tab("connection.owner", "Owner", "Identifies the connection's owning process, executable and user where accessible. Creation time prevents joining a reused PID to the wrong owner."),
        Tab("process.threads", "Threads", "Shows accessible threads and their execution state, CPU time and priority. Useful when investigating responsiveness and execution behavior.", platform: PlatformKind.Windows),
        Tab("process.modules", "Modules", "Lists accessible loaded modules and paths. Useful for checking dependencies and unexpected components; a module name alone does not prove trust.", platform: PlatformKind.Windows),
        Tab("process.services", "Services", "Lists Windows services associated with the verified process. Shared-host processes can own several services.", platform: PlatformKind.Windows),
        Tab("process.connections", "Connections", "Shows endpoints associated with the selected process and creation identity. These are snapshots; per-process bandwidth and application intent are unavailable."),
        Tab("process.performance", "Performance", "Shows the selected process's captured counters and rates. This inspection tab is a snapshot rather than continuous monitoring."),
        Tab("process.handles", "Handles", "Shows the captured handle count where available. Individual open-handle enumeration is not implemented."),
        Tab("device.hardware", "Hardware", "Shows OS-reported hardware and compatible identifiers. Useful for identifying components and finding appropriate drivers.", "An instance-path suffix is not assumed to be a physical serial number. Unqueried USB descriptor fields remain unavailable.", PlatformKind.Windows),
        Tab("device.connection", "Connection", "Shows reported enumeration, location and removable-device properties. Removable capability does not establish permission to eject or write this device.", platform: PlatformKind.Windows),
        Tab("device.driver", "Driver", "Shows available driver associations, service and metadata. Useful for diagnosing driver configuration; this tab performs no driver changes.", platform: PlatformKind.Windows),
        Tab("device.topology", "Topology", "Shows confirmed parent and child device relationships. Open a related row to inspect it; this is the Windows device hierarchy rather than a physical cable map.", platform: PlatformKind.Windows),
        Tab("device.events", "Events", "Shows local device events matching the exact instance identifier. Initial inventory is a baseline, not proof of a connection event.", platform: PlatformKind.Windows),
        Tab("device.storage", "Storage", "Shows storage associations established by device identity and native ancestry. Open mapped disks or volumes to follow the relationship.", platform: PlatformKind.Windows),
        Tab("disk.layout", "Layout", "Shows partitions and volume associations exposed by Windows. Unlettered partitions may have no logical-volume mapping.", platform: PlatformKind.Windows),
        Tab("disk.health", "Health", "Shows available provider reliability fields. Inventory status is not SMART certification; missing temperatures or counters remain unavailable.", platform: PlatformKind.Windows),
        Tab("disk.events", "Events", "Shows bounded shared Windows storage events. Events are not guaranteed to belong to this disk; use their source and message for investigation.", platform: PlatformKind.Windows),
        Tab("disk.performance", "Performance", "Shows the selected disk's on-demand performance counters where available. Provider availability and sampling affect these values."),
        Tab("adapter.interface", "Interface", "Shows the selected adapter's addressing, gateways, DNS, link properties and counters. Link capacity is not measured throughput."),
        Tab("adapter.connections", "Connections", "Shows endpoints whose concrete local address matches this adapter. Wildcard listeners cannot be assigned to one interface from these snapshots.", "Interface traffic covers all owners. These rows do not attribute interface bytes to individual processes or prove which route a remote packet used."),
        Tab("file.dates", "Dates", "Shows filesystem-reported timestamps for the selected object. Timestamp meaning depends on the filesystem and is not proof of user activity."),
        Tab("file.security", "Security", "Shows the owner SID and available access-control rules. This read-only display is not an effective-access grant.", platform: PlatformKind.Windows),
        Tab("file.checksums", "Checksums", "File contents are read only when you request a checksum. Identity and metadata are checked before and after hashing; directories cannot be hashed here."),
        Tab("file.usage", "Usage", "Shows available allocation information and directs directory totals to an explicit scan. Ordinary property inspection does not recurse folders."),
        Tab("file.operations", "Operations", "Explains the current file-operation policy and recovery limits. Actual operations use the separate review and identity-validation workflow."),
        Tab("advanced", "Advanced", "Shows additional technical identifiers and raw provider properties. Unavailable fields remain explicit; this tab does not perform configuration changes.")
    });
    private static readonly IReadOnlyDictionary<string, InspectionDescription> ById = Tabs.ToDictionary(t => t.Id, StringComparer.Ordinal);
    public static InspectionDescription? Find(string id) => ById.GetValueOrDefault(id);
    public static InspectionDescription Resolve(InspectionScope scope, string title)
    {
        var id = (scope, title) switch {
            (InspectionScope.Process, "General") => "process.general", (_, "Threads") => "process.threads", (_, "Modules") => "process.modules", (_, "Services") => "process.services",
            (_, "Owner") => "connection.owner", (InspectionScope.Device, "Connection") => "device.connection", (_, "Connection") => "connection.endpoint",
            (InspectionScope.Process, "Connections") => "process.connections", (InspectionScope.Adapter, "Connections") => "adapter.connections",
            (_, "Hardware") => "device.hardware", (_, "Driver") => "device.driver", (_, "Topology") => "device.topology", (_, "Storage") => "device.storage",
            (InspectionScope.Disk, "Events") => "disk.events", (_, "Events") => "device.events", (_, "Layout") => "disk.layout", (_, "Health") => "disk.health",
            (InspectionScope.Disk, "Performance") => "disk.performance", (_, "Performance") => "process.performance", (_, "Handles") => "process.handles",
            (_, "Interface") => "adapter.interface", (_, "Dates") => "file.dates", (_, "Security") => "file.security", (_, "Checksums") => "file.checksums",
            (_, "Usage") => "file.usage", (_, "Operations") => "file.operations", (_, "Advanced") => "advanced", _ => "general"
        };
        return ById[id];
    }
    public static bool Available(InspectionDescription tab, PlatformKind platform) => tab.Platform == null || tab.Platform == platform;
}

// UI-independent selection state: tab changes update a small metadata reference only.
public sealed class InspectionContext(InspectionScope scope, PlatformKind platform)
{
    public InspectionDescription Current { get; private set; } = InspectionDescriptions.Resolve(scope, "General");
    public bool Select(string title) {
        return SelectId(InspectionDescriptions.Resolve(scope, title).Id);
    }
    public bool SelectId(string id) {
        var next = InspectionDescriptions.Find(id);
        if (next == null) return false;
        if (!InspectionDescriptions.Available(next, platform)) return false;
        Current = next; return true;
    }
}
