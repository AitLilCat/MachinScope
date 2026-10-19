using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
namespace Core.Windows.Hardware;

public sealed class DiskInspector : IDiskInspector
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";
    private readonly SemaphoreSlim gate = new(1, 1);
    private static ulong? Size(string value) => ulong.TryParse(value, out var size) ? size : null;
    private static bool? Flag(string value) => bool.TryParse(value, out var flag) ? flag : null;
    private static string Reference(string value)
    {
        var match = Regex.Match(value, "DeviceID=\"(.*)\"$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\") : value;
    }
    public async Task<DiskInventory> InventoryAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await Task.Run(() => Inventory(token), token); }
        finally { gate.Release(); }
    }
    private static DiskInventory Inventory(CancellationToken token)
    {
        var issues = new List<string>();
        List<string[]> Read(string scope, string query, params string[] fields)
        {
            try { return SystemInspector.Query(scope, query, fields, token); }
            catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { issues.Add(query + ": " + e.Message); return []; }
        }
        var driveRows = Read(@"root\cimv2", "SELECT * FROM Win32_DiskDrive", "Index", "DeviceID", "Model", "SerialNumber", "Size", "FirmwareRevision", "InterfaceType", "Status");
        var nativeDisks = Read(StorageScope, "SELECT * FROM MSFT_Disk", "Number", "UniqueId", "IsSystem", "IsBoot", "LogicalSectorSize", "PhysicalSectorSize", "PartitionStyle", "IsReadOnly", "IsOffline", "BusType");
        var partitions = Read(@"root\cimv2", "SELECT * FROM Win32_DiskPartition", "DiskIndex", "Index", "DeviceID", "Type", "StartingOffset", "Size", "BootPartition");
        var nativePartitions = Read(StorageScope, "SELECT * FROM MSFT_Partition", "DiskNumber", "PartitionNumber", "Offset", "Size", "GptType", "IsSystem", "IsBoot");
        var links = Read(@"root\cimv2", "SELECT * FROM Win32_LogicalDiskToPartition", "Antecedent", "Dependent");
        var volumeRows = Read(@"root\cimv2", "SELECT * FROM Win32_Volume", "DriveLetter", "DeviceID", "Label", "FileSystem", "Capacity", "FreeSpace", "SystemVolume", "BootVolume");
        var pageFiles = Read(@"root\cimv2", "SELECT * FROM Win32_PageFileUsage", "Name");
        var volumes = volumeRows.Select(r => new VolumeTarget(r[0] == "Unavailable" ? r[1] : r[0] + "\\", r[1], r[2], r[3], Size(r[4]), Size(r[5]), Flag(r[6]), Flag(r[7]), pageFiles.Any(p => p[0].StartsWith(r[0] + "\\", StringComparison.OrdinalIgnoreCase)))).ToArray();
        var complete = issues.Count == 0;
        var disks = new List<InspectedDisk>();
        foreach (var r in driveRows)
        {
            token.ThrowIfCancellationRequested(); if (!int.TryParse(r[0], out var number) || Size(r[4]) is not { } capacity) { issues.Add("Disk identity or capacity unavailable: " + r[1]); continue; }
            var metadata = nativeDisks.FirstOrDefault(d => d[0] == r[0]);
            string PartitionType(string[] p) { var native = nativePartitions.FirstOrDefault(n => n[0] == p[0] && n[2] == p[4]); var kind = native?[4]?.Trim('{', '}').ToLowerInvariant(); return kind switch { "c12a7328-f81f-11d2-ba4b-00a0c93ec93b" => "EFI System", "de94bba4-06d1-4d40-a16a-bfd50179d6ac" => "Windows Recovery", _ => p[3] }; }
            bool PartitionBoot(string[] p) { var native = nativePartitions.FirstOrDefault(n => n[0] == p[0] && n[2] == p[4]); return Flag(p[6]) == true || (native != null && (Flag(native[5]) == true || Flag(native[6]) == true)); }
            var diskPartitions = partitions.Where(p => p[0] == r[0]).Select(p => new PartitionTarget(number, int.TryParse(p[1], out var n) ? n : -1, p[2], PartitionType(p), Size(p[4]), Size(p[5]), PartitionBoot(p), links.Where(l => Reference(l[0]).Equals(p[2], StringComparison.OrdinalIgnoreCase)).SelectMany(l => volumes.Where(v => v.Path.TrimEnd('\\').Equals(Reference(l[1]), StringComparison.OrdinalIgnoreCase))).ToArray())).ToArray();
            var protection = DiskProtectionPolicy.Classify(metadata == null ? null : Flag(metadata[2]), metadata == null ? null : Flag(metadata[3]), diskPartitions, complete);
            string[] names = ["Device path", "Model", "Serial", "Capacity bytes", "Firmware", "Interface", "Provider status"];
            var raw = names.Select((name, i) => (name, value: r[i + 1])).ToDictionary(x => x.name, x => x.value);
            if (metadata != null) { string[] keys = ["Disk number", "Unique ID", "Is system", "Is boot", "Logical sector bytes", "Physical sector bytes", "Partition style code", "Read only", "Offline", "Bus type code"]; for (var i = 0; i < keys.Length; i++) raw[keys[i]] = metadata[i]; }
            disks.Add(new(new(number, r[1], r[2], r[3].Trim(), capacity, metadata?[1]), r[5], r[6], r[7], diskPartitions, protection.State, protection.Reasons, raw));
        }
        return new(disks, volumes, issues);
    }
    public async Task<DiskDetails> DetailsAsync(DiskTarget target, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await Task.Run(() => Details(target, token), token); }
        finally { gate.Release(); }
    }
    private static DiskDetails Details(DiskTarget target, CancellationToken token)
    {
        var inventory = Inventory(token);
        var disk = inventory.Disks.SingleOrDefault(d => d.Target.Number == target.Number && d.Target.Identity == target.Identity) ?? throw new InvalidOperationException("Disk identity changed or the disk is no longer available. Refresh Storage before inspecting.");
        static InspectionTable Values(string title, IEnumerable<string[]> rows, string? notice = null) => new(title, ["Property", "Value"], rows.ToArray(), notice);
        var general = Values("General", [["Model", target.Model], ["Device", target.DevicePath], ["Serial", target.Serial], ["Capacity", Calculations.Bytes(target.Capacity)], ["Firmware", disk.Firmware], ["Interface", disk.Bus], ["Provider status", disk.ProviderStatus], ["Protection", disk.Protection.ToString()], ["Protection reasons", string.Join("; ", disk.ProtectionReasons)]], "Provider status is an inventory report, not a SMART certification.");
        var layoutRows = new List<InspectionRow>();
        foreach (var partition in disk.Partitions) {
            foreach (var volume in partition.Volumes.Cast<VolumeTarget?>().DefaultIfEmpty(null)) {
                string[] cells = [partition.DeviceId, partition.Type, partition.Offset?.ToString() ?? "Unavailable", partition.Capacity.HasValue ? Calculations.Bytes(partition.Capacity.Value) : "Unavailable", volume?.Path ?? "No drive-letter volume association", volume?.FileSystem ?? "—", volume?.Label ?? "—"];
                layoutRows.Add(new($"{target.Identity}|{partition.DeviceId}|{volume?.DeviceId}", cells, cells, new Dictionary<string, object?> { ["name"] = volume?.Label ?? partition.DeviceId }, (object?)volume ?? partition));
            }
        }
        var layout = new InspectionTable("Layout", ["Partition", "Type", "Offset B", "Capacity", "Volume path", "Filesystem", "Label"], layoutRows.Select(r => r.Cells).ToArray(), "Windows WMI associations; unlettered partitions may not expose a logical-volume association. Open a row to inspect its captured volume or partition.", layoutRows);
        var health = Health(disk, token);
        var performance = new SystemInspector(Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemInspector>.Instance).ResourceAsync("Disk " + target.Number, token).GetAwaiter().GetResult() with { Title = "Performance" };
        var advanced = Values("Advanced", disk.Raw.Select(r => new[] { r.Key, r.Value }), "Exact provider values. TRIM, caching and raw NVMe/SMART decoding require additional providers.");
        var events = Events(target, token);
        return new(target, [general, layout, health, performance, advanced, events]);
    }
    private static InspectionTable Health(InspectedDisk disk, CancellationToken token)
    {
        var rows = new List<string[]> { new[] { "Inventory status", disk.ProviderStatus, "Win32_DiskDrive; not SMART health" } };
        try
        {
            // Match the provider unique ID and serial; legacy Win32 capacity may be geometrically rounded.
            // Never assume MSFT_PhysicalDisk.DeviceId equals the Win32 disk index.
            using var searcher = new ManagementObjectSearcher(StorageScope, "SELECT * FROM MSFT_PhysicalDisk") { Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10) } };
            using var objects = searcher.Get(); var matches = new List<string>();
            foreach (ManagementObject item in objects) using (item) { token.ThrowIfCancellationRequested(); if (disk.Target.Serial.Length > 0 && disk.Target.Serial != "Unavailable" && SystemInspector.Value(item, "SerialNumber").Trim().Equals(disk.Target.Serial, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(disk.Target.UniqueId) && disk.Target.UniqueId != "Unavailable" && SystemInspector.Value(item, "UniqueId").Equals(disk.Target.UniqueId, StringComparison.OrdinalIgnoreCase)) matches.Add(item.Path.RelativePath); }
            if (matches.Count != 1) rows.Add(["Reliability counters", "Unavailable", "Physical provider identity cannot be correlated uniquely"]);
            else
            {
                string[] fields = ["Temperature", "TemperatureMax", "Wear", "PowerOnHours", "ReadErrorsTotal", "ReadErrorsUncorrected", "WriteErrorsTotal", "WriteErrorsUncorrected", "ReadLatencyMax", "WriteLatencyMax", "FlushLatencyMax"];
                var counters = SystemInspector.Query(StorageScope, "ASSOCIATORS OF {" + matches[0] + "} WHERE AssocClass=MSFT_PhysicalDiskToStorageReliabilityCounter", fields, token);
                if (counters.Count == 0) rows.Add(["Reliability counters", "Unavailable", "Provider returned no associated counters"]);
                foreach (var values in counters) for (var i = 0; i < fields.Length; i++) rows.Add([fields[i], values[i], "MSFT_StorageReliabilityCounter"]);
                rows.Add(["Correlated physical provider", matches[0], "Provider unique ID + serial match"]);
            }
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { rows.Add(["Reliability provider", "Unavailable", e.Message]); }
        return new("Health", ["Metric", "Raw value", "Source"], rows, "Provider reliability fields are not a decoded SMART/NVMe report. Missing fields remain unavailable; a zero is shown only when returned by the provider.");
    }
    private static InspectionTable Events(DiskTarget target, CancellationToken token)
    {
        var rows = new List<string[]>();
        try
        {
            var since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-1));
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", $"SELECT * FROM Win32_NTLogEvent WHERE Logfile='System' AND TimeGenerated>='{since}' AND (SourceName='disk' OR SourceName='Ntfs' OR SourceName='stornvme' OR SourceName='storahci')") { Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true } };
            using var events = searcher.Get(); foreach (ManagementBaseObject item in events) using (item) { token.ThrowIfCancellationRequested(); rows.Add([SystemInspector.Value(item, "TimeGenerated"), SystemInspector.Value(item, "SourceName"), SystemInspector.Value(item, "EventCode"), SystemInspector.Value(item, "Message")]); if (rows.Count == 100) break; }
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { rows.Add(["Unavailable", "Provider", "—", e.Message]); }
        return new("Events", ["Time (DMTF)", "Source", "Event ID", "Message"], rows, "System storage events from the last 24 hours, capped at 100. These are shared system events; attribution to this disk is not guaranteed.");
    }
}
