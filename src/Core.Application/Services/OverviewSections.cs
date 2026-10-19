using Core.Domain.Models;
namespace Core.Application.Services;

public sealed record OverviewSection(string Name, string Detail, NavigationDestination Destination);
public sealed record OverviewSnapshot(SystemSample? Sample, IReadOnlyList<ProcessSample> Processes, InspectionTable? Inventory,
    DiskInventory? Storage, DeviceSnapshot? Devices, InspectionTable? Adapters, int? ConnectionCount);
public static class OverviewSections
{
    public static IReadOnlyList<OverviewSection> Build(NavigationDestination destination, OverviewSnapshot snapshot)
    {
        var sample = snapshot.Sample;
        string Inventory(string name) => string.Join("; ", snapshot.Inventory?.Rows.Where(r => r[0] == name).Select(r => r[1]) ?? []).Trim() is { Length: > 0 } value ? value : "Not inspected yet";
        var activity = sample == null ? "Waiting for a system sample" : $"CPU: {(sample.CpuPercent is { } cpu ? $"{cpu:0.0}%" : "Sampling")} · Processes: {sample.ProcessCount:N0} · Uptime: {sample.Uptime:dd\\.hh\\:mm\\:ss}";
        var memory = sample == null ? "Waiting for a system sample" : sample.TotalMemory == 0 || sample.AvailableMemory > sample.TotalMemory ? "Memory values unavailable or inconsistent" : $"{Calculations.Bytes(sample.TotalMemory - sample.AvailableMemory)} used / {Calculations.Bytes(sample.TotalMemory)} physical memory";
        var storage = snapshot.Storage == null ? "Detected models: " + Inventory("Physical disk") + ". Reliability has not been inspected." : $"{snapshot.Storage.Disks.Count} physical disks · {snapshot.Storage.Volumes.Count} volumes · {snapshot.Storage.Issues.Count} provider issues · {snapshot.Storage.Volumes.Count(v => v.Capacity is > 0 && v.Free.HasValue && (double)v.Free.Value / v.Capacity.Value < 0.1)} volumes below 10% free. Provider status is not decoded SMART health.";
        var devices = snapshot.Devices == null ? "Not inspected yet" : $"{snapshot.Devices.Devices.Count} present devices · {snapshot.Devices.Devices.Count(d => d.UsbRelated)} USB-related · {snapshot.Devices.Devices.Count(d => d.ProblemCode is > 0)} reported problems. {snapshot.Devices.NotificationStatus}";
        var top = snapshot.Processes.Count == 0 ? "Open Processes to inspect individual workloads." : string.Join("; ", snapshot.Processes.OrderByDescending(p => p.CpuPercent ?? 0).ThenByDescending(p => p.WorkingSet ?? 0).Take(3).Select(p => $"{p.Name} (PID {p.Id}): {(p.CpuPercent.HasValue ? $"{p.CpuPercent:0.0}% CPU" : "CPU sampling")}, {(p.WorkingSet is { } workingSet ? Calculations.Bytes(workingSet) + " RAM" : "RAM unavailable")}"));
        var adapters = snapshot.Adapters?.Data?.Where(r => r.Raw.ElementAtOrDefault(2) == "Up").ToArray();
        var network = adapters == null ? "Not inspected yet" : string.Join("\n", adapters.Take(3).Select(r => $"{r.Cells[0]} · {r.Cells[5]} · Gateway {r.Cells[6]} · DNS {r.Cells[7]}"));
        if (adapters is { Length: 0 }) network = "No adapter reports an Up state.";
        network += snapshot.ConnectionCount.HasValue ? $"\n{snapshot.ConnectionCount} observed endpoints." : "\nEndpoint count not inspected.";
        if (sample != null) network += $" Receive {Calculations.Bytes(sample.ReceiveRate)}/s · Send {Calculations.Bytes(sample.SendRate)}/s.";
        return destination switch {
            NavigationDestination.CoreOverview => [new("Computer", Inventory("Computer") + " · " + Inventory("Windows"), NavigationDestination.OperatingSystem), new("CPU", Inventory("CPU") + "\n" + activity, NavigationDestination.Performance), new("Memory", memory, NavigationDestination.Performance), new("Last sampled workloads", top, NavigationDestination.Processes), new("Storage", storage, NavigationDestination.DisksVolumes), new("Network", network, NavigationDestination.Adapters)],
            NavigationDestination.ActivityOverview => [new("System activity", activity + "\n" + memory, NavigationDestination.Performance), new("Last sampled resource-intensive processes", top, NavigationDestination.Processes), new("Startup & Services", "Startup inventory, read-only service status and reviewed current-user startup actions.", NavigationDestination.Startup)],
            NavigationDestination.HardwareOverview => [new("Storage", storage, NavigationDestination.DisksVolumes), new("Reliability", "Open per-disk properties for available reliability counters, temperatures and storage events. Missing fields are unavailable; no inferred health percentage.", NavigationDestination.DiskHealth), new("Connected hardware", devices, NavigationDestination.Devices), new("Files & Cleanup", "Browse files, analyze sizes/errors and confirm duplicates before reviewed file operations.", NavigationDestination.Files)],
            NavigationDestination.NetworkOverview => [new("Adapters and addressing", network, NavigationDestination.Adapters), new("Connections", "Inspect observed TCP/UDP endpoints and process ownership. Incoming/outgoing attribution and per-process byte rates are unsupported.", NavigationDestination.Connections), new("Traffic", "Interface throughput and counters; not packet capture.", NavigationDestination.Traffic), new("Diagnostics", "Explicit DNS, reverse DNS, ping, TCP and traceroute requests.", NavigationDestination.Diagnostics)],
            NavigationDestination.ConfigurationOverview => [new("Operating system", Inventory("Windows"), NavigationDestination.OperatingSystem), new("Security & Permissions", "Read-only account, privilege and capability information. Firewall state is unsupported.", NavigationDestination.Security), new("Maintenance", "Supported diagnostic and maintenance entry points.", NavigationDestination.Maintenance), new("Startup & Services", "Canonical location: System Activity.", NavigationDestination.Startup)],
            NavigationDestination.Maintenance => [new("Network diagnostics", "Run connectivity and DNS checks only when requested.", NavigationDestination.Diagnostics), new("Disk diagnostics", "Provider reliability fields and labeled system storage events.", NavigationDestination.DiskHealth), new("File diagnostics", "Scan traversal errors and analyze file storage without following reparse points.", NavigationDestination.Files)],
            _ => [] };
    }
}
