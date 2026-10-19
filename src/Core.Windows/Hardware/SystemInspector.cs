using System.Diagnostics;
using System.ComponentModel;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Core.Application.Interfaces;
using Core.Domain.Models;
using Core.Windows.Native;
using Core.Windows.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
namespace Core.Windows.Hardware;

public sealed partial class SystemInspector(ILogger<SystemInspector> logger) : ISystemInspector
{
    private readonly Core.Application.Services.ConnectionTracker connections = new();
    private readonly object connectionLock = new();
    private (ulong Idle, ulong Kernel, ulong User)? previousCpu;
    private readonly Dictionary<int, (DateTime Start, double Cpu, long Read, long Write, long Tick)> processHistory = new();
    private readonly Dictionary<string, (long In, long Out, long Tick)> networkHistory = new();
    private readonly long frequency = Stopwatch.Frequency;
    public SystemSample Sample()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var percent = previousCpu is { } p ? Calculations.Cpu(idle, kernel, user, p.Idle, p.Kernel, p.User) : null;
        previousCpu = (idle, kernel, user);
        var memory = new NativeMethods.MemoryStatus { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var now = Stopwatch.GetTimestamp(); double incoming = 0, outgoing = 0;
        var live = new HashSet<string>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            live.Add(adapter.Id);
            try { var s = adapter.GetIPStatistics(); if (networkHistory.TryGetValue(adapter.Id, out var last) && adapter.OperationalStatus == OperationalStatus.Up) { var seconds = (now - last.Tick) / (double)frequency; incoming += Calculations.Rate(s.BytesReceived, last.In, seconds) ?? 0; outgoing += Calculations.Rate(s.BytesSent, last.Out, seconds) ?? 0; } networkHistory[adapter.Id] = (s.BytesReceived, s.BytesSent, now); }
            catch (NetworkInformationException) { }
        }
        foreach (var key in networkHistory.Keys.Where(k => !live.Contains(k)).ToArray()) networkHistory.Remove(key);
        var processes = Process.GetProcesses(); var count = processes.Length; foreach (var process in processes) process.Dispose();
        return new(DateTimeOffset.Now, percent, memory.TotalPhysical, memory.AvailablePhysical, memory.TotalPageFile - memory.AvailablePageFile, memory.TotalPageFile, count, TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64()), incoming, outgoing);
    }
    private static Dictionary<int, int> Parents()
    {
        var result = new Dictionary<int, int>(); using var handle = NativeMethods.CreateToolhelp32Snapshot(2, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new NativeMethods.ProcessEntry { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>(), Exe = "" };
        if (NativeMethods.Process32FirstW(handle, ref entry)) do { result[(int)entry.Id] = (int)entry.ParentId; } while (NativeMethods.Process32NextW(handle, ref entry));
        return result;
    }
    private readonly object processLock = new();
    private readonly Dictionary<string, ProcessMetadata> metadataCache = new();
    public IReadOnlyList<ProcessSample> Processes() { lock (processLock) return ReadProcesses(); }
    private IReadOnlyList<ProcessSample> ReadProcesses()
    {
        var parents = Parents(); var result = new List<ProcessSample>(); var now = Stopwatch.GetTimestamp(); var live = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var id = process.Id; live.Add(id); var name = process.ProcessName; DateTime? start = null; string? path = null, error = null; double? cpu = null, read = null, write = null; int? handles = null; var priority = "Unavailable";
                    long? rss = null, privateBytes = null; int? threads = null; TimeSpan? totalCpu = null; ProcessMetadata? metadata = null;
                    try { rss = process.WorkingSet64; privateBytes = process.PrivateMemorySize64; threads = process.Threads.Count; handles = process.HandleCount; } catch (Exception e) when (Expected(e)) { error = e.Message; }
                    try { start = process.StartTime; priority = process.PriorityClass.ToString(); } catch (Exception e) when (Expected(e)) { error = e.Message; }
                    var metadataKey = $"{id}:{start?.ToUniversalTime().Ticks}"; metadata = metadataCache.GetValueOrDefault(metadataKey) ?? ProcessMetadata.Read(id); if (metadata != null && metadata.Started == start) { path = metadata.Path; metadataCache[metadataKey] = metadata; } else metadata = null;
                    if (start.HasValue)
                    {
                        try
                        {
                            totalCpu = process.TotalProcessorTime; var cpuTime = totalCpu.Value.TotalSeconds; long r = -1, w = -1;
                            using var handle = NativeMethods.OpenProcess(0x1000, false, (uint)id);
                            if (!handle.IsInvalid && NativeMethods.GetProcessIoCounters(handle, out var io)) { r = (long)io.ReadBytes; w = (long)io.WriteBytes; }
                            if (processHistory.TryGetValue(id, out var last) && last.Start == start.Value)
                            {
                                var seconds = (now - last.Tick) / (double)frequency;
                                cpu = seconds > 0 ? Math.Clamp(100 * (cpuTime - last.Cpu) / seconds / Environment.ProcessorCount, 0, 100) : null;
                                if (r >= 0 && last.Read >= 0) read = Calculations.Rate(r, last.Read, seconds);
                                if (w >= 0 && last.Write >= 0) write = Calculations.Rate(w, last.Write, seconds);
                            }
                            processHistory[id] = (start.Value, cpuTime, r, w, now);
                        }
                        catch (Exception e) when (Expected(e)) { error = e.Message; }
                    }
                    result.Add(new(id, parents.GetValueOrDefault(id), name, start, path, cpu, rss, privateBytes, threads, handles, priority, read, write, error) { User = metadata?.User, Architecture = metadata?.Architecture, Integrity = metadata?.Integrity, CpuTime = totalCpu });
                }
                catch (Exception e) when (Expected(e)) { logger.LogDebug(e, "Process exited during snapshot"); }
            }
        }
        foreach (var id in processHistory.Keys.Where(k => !live.Contains(k)).ToArray()) processHistory.Remove(id);
        var identities = result.Where(p => p.Started.HasValue).Select(p => p.Identity).ToHashSet(); foreach (var key in metadataCache.Keys.Where(k => !identities.Contains(k)).ToArray()) metadataCache.Remove(key);
        return result;
    }
    private static bool Expected(Exception e) => e is Win32Exception or InvalidOperationException or NotSupportedException;
    internal static string Value(ManagementBaseObject o, string property) { try { return o[property]?.ToString() ?? "Unavailable"; } catch (ManagementException) { return "Unavailable"; } }
    internal static List<string[]> Query(string scope, string query, string[] properties, CancellationToken token)
    {
        var session = StorageQuerySession.Current; var key = scope + "|" + query;
        if (session != null && session.Queries.TryGetValue(key, out var cached)) { session.CacheHits++; token.ThrowIfCancellationRequested(); return cached.Select(row => properties.Select(p => row.GetValueOrDefault(p, "Unavailable")).ToArray()).ToList(); }
        var rows = new List<string[]>(); var snapshot = session == null ? null : new List<Dictionary<string, string>>();
        using var searcher = new ManagementObjectSearcher(scope, query) { Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true } };
        using var objects = searcher.Get(); foreach (ManagementBaseObject o in objects) using (o) {
            token.ThrowIfCancellationRequested(); rows.Add(properties.Select(p => Value(o, p)).ToArray());
            if (snapshot != null) { var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (PropertyData p in o.Properties) raw[p.Name] = Value(o, p.Name); snapshot.Add(raw); }
        }
        token.ThrowIfCancellationRequested(); if (snapshot != null) session!.Queries[key] = snapshot;
        return rows;
    }
    public Task<InspectionTable> OverviewAsync(CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<string[]> { new[] { "Computer", Environment.MachineName }, new[] { "Windows", RuntimeInformation.OSDescription }, new[] { "Logical processors", Environment.ProcessorCount.ToString() }, new[] { "Application architecture", RuntimeInformation.ProcessArchitecture.ToString() } };
        foreach (var spec in new[] { ("CPU", "Win32_Processor", "Name"), ("Physical cores (per socket)", "Win32_Processor", "NumberOfCores"), ("GPU", "Win32_VideoController", "Name"), ("Physical disk", "Win32_DiskDrive", "Model") })
            try { foreach (var r in Query("root\\cimv2", $"SELECT {spec.Item3} FROM {spec.Item2}", [spec.Item3], token)) rows.Add([spec.Item1, r[0]]); } catch (ManagementException e) { rows.Add([spec.Item1, "Unavailable: " + e.Message]); }
        try { rows.Add(["Running services", Query("root\\cimv2", "SELECT Name FROM Win32_Service WHERE State='Running'", ["Name"], token).Count.ToString()]); } catch (ManagementException e) { rows.Add(["Running services", e.Message]); }
        return new InspectionTable("System inventory", ["Property", "Value"], rows, "Inventory is read on navigation or manual refresh. GPU usage, temperature and per-process network rates are unavailable.");
    }, token);
    public Task<InspectionTable> PerformanceAsync(CancellationToken token) => Task.Run(() =>
    {
        string[] cols = ["Name", "PercentProcessorTime", "PercentPrivilegedTime", "PercentUserTime", "ProcessorFrequency"];
        var rows = Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation", cols, token);
        var memory = Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_PerfOS_Memory", ["AvailableBytes", "CacheBytes", "CommittedBytes", "CommitLimit", "PagesPersec"], token);
        foreach (var r in memory) { rows.Add(["Memory available / cached / committed / limit / pages per sec", .. r]); }
        return new InspectionTable("CPU and memory counters", ["Processor / memory", "CPU % / available B", "Kernel % / cache B", "User % / commit B", "MHz / limit B", "Pages/sec"], rows.Select(r => r.Length == 5 ? r.Concat(new[] { "—" }).ToArray() : r).ToArray(), "Counters are provider-reported. Refresh manually; live total CPU/RAM and bounded history continue above.");
    }, token);
    public Task<InspectionTable> StorageAsync(CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<string[]>();
        string[] diskProps = ["Index", "Model", "SerialNumber", "InterfaceType", "Size", "FirmwareRevision", "Partitions", "Status"];
        foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_DiskDrive", diskProps, token)) rows.Add(["Physical disk", r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7]]);
        foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_DiskPartition", ["DiskIndex", "Index", "Name", "Type", "Size", "StartingOffset"], token)) rows.Add(["Partition", $"Disk {r[0]} / {r[1]}", r[2], "—", r[3], r[4], $"Offset {r[5]}", "—", "Unavailable"]);
        foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_LogicalDisk", ["DeviceID", "VolumeName", "FileSystem", "Size", "FreeSpace"], token)) rows.Add(["Volume", r[0], r[1], "—", r[2], r[3], $"Free {r[4]} B", "—", "Unavailable"]);
        foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_LogicalDiskToPartition", ["Antecedent", "Dependent"], token)) rows.Add(["Partition → volume", r[0], r[1], "—", "—", "—", "—", "—", "—"]);
        try { foreach (var r in Query("root\\Microsoft\\Windows\\Storage", "SELECT * FROM MSFT_PhysicalDisk", ["DeviceId", "FriendlyName", "SerialNumber", "BusType", "Size", "MediaType", "HealthStatus"], token)) rows.Add(["Storage provider", r[0], r[1], r[2], $"Bus code {r[3]}", r[4], $"Media code {r[5]}", "—", $"Provider code {r[6]}"]); }
        catch (ManagementException e) { rows.Add(["Storage provider", "Unavailable", e.Message, "—", "—", "—", "—", "—", "—"]); }
        try { foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk", ["Name", "DiskReadBytesPersec", "DiskWriteBytesPersec", "DiskReadsPersec", "DiskWritesPersec", "CurrentDiskQueueLength", "PercentDiskTime"], token)) rows.Add(["Disk counters", r[0], $"Read {r[1]} B/s; write {r[2]} B/s", "—", $"IOPS {r[3]} / {r[4]}", "—", $"Queue {r[5]}", "—", $"Time {r[6]}%"]); }
        catch (ManagementException e) { rows.Add(["Disk counters", "Unavailable", e.Message, "—", "—", "—", "—", "—", "—"]); }
        var diskMetadata = new Dictionary<string, string[]>();
        try { foreach (var r in Query("root\\Microsoft\\Windows\\Storage", "SELECT * FROM MSFT_Disk", ["Number", "PartitionStyle", "LogicalSectorSize", "PhysicalSectorSize", "IsReadOnly", "IsOffline"], token)) diskMetadata[r[0]] = r; } catch (ManagementException) { }
        var volumeMetadata = new Dictionary<string, string[]>();
        try { foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_Volume", ["DriveLetter", "BlockSize", "FreeSpace", "Capacity", "DeviceID"], token)) volumeMetadata[r[0]] = r; } catch (ManagementException) { }
        for (var i = 0; i < rows.Count; i++) { var r = rows[i]; var extra = Enumerable.Repeat("Unavailable", 5).ToArray();
            if (r[0] == "Physical disk" && diskMetadata.TryGetValue(r[1], out var d)) extra = [d[1] switch { "0" => "RAW", "1" => "MBR", "2" => "GPT", _ => "Unknown" }, d[2], d[3], $"Read only: {d[4]}; offline: {d[5]}", "—"];
            if (r[0] == "Volume" && volumeMetadata.TryGetValue(r[1], out var v)) { extra = ["—", "—", "—", "—", $"Allocation unit {v[1]} B; volume {v[4]}"]; if (ulong.TryParse(v[2], out var free) && ulong.TryParse(v[3], out var size)) r[6] = $"Used {Calculations.Bytes(size >= free ? size - free : 0)}; free {Calculations.Bytes(free)}"; }
            if (r[0] == "Partition → volume") { static string Reference(string text) { var m = System.Text.RegularExpressions.Regex.Match(text, "DeviceID=\"(.*)\"$"); return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : text; } r[1] = Reference(r[1]); r[2] = Reference(r[2]); }
            rows[i] = r.Concat(extra).ToArray();
        }
        return new InspectionTable("Physical disks, partitions and volumes", ["Kind", "Identity", "Model / label / association", "Serial", "Interface / filesystem", "Capacity B", "Firmware / free / offset", "Partitions", "Reported status", "Partition style", "Logical sector B", "Physical sector B", "Disk flags", "Allocation / volume path"], rows, "Provider status is not a SMART health guarantee. SMART/NVMe health, temperatures and encryption are unavailable. No destructive disk operations.");
    }, token);
    private StartupController? startup;
    private StartupController Startup => startup ??= new StartupController(logger);
    public void SetStartupEnabled(StartupEntry entry, bool enabled) => Startup.SetEnabled(entry, enabled);
    public Task<InspectionTable> StartupAsync(CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<string[]>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine }) foreach (var name in new[] { "Run", "RunOnce" }) foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested(); using var root = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\" + name);
            if (key != null && !(hive == Registry.CurrentUser && name == "Run" && view == RegistryView.Registry64)) foreach (var value in key.GetValueNames()) rows.Add([value, key.GetValue(value)?.ToString() ?? "Unavailable", $"Registry {name} {view}", hive == Registry.CurrentUser ? "User" : "System", "Configured; StartupApproved override not interpreted"]);
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        { var path = Environment.GetFolderPath(folder); if (Directory.Exists(path)) foreach (var entry in Directory.EnumerateFiles(path)) rows.Add([Path.GetFileName(entry), entry, "Startup folder", folder == Environment.SpecialFolder.Startup ? "User" : "System", "Present; shortcut target not resolved"]); }
        foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_Service WHERE StartMode='Auto'", ["Name", "PathName", "StartName", "State"], token)) rows.Add([r[0], r[1], "Automatic service", r[2], r[3]]);
        var managed = Startup.Read(); var data = new List<InspectionRow>();
        foreach (var entry in managed) { string[] cells = [entry.Name, entry.Command, "Registry Run Registry64", "User", entry.Disabled ? "Disabled by MachinScope · recovery backup retained" : "Configured · Windows may apply startup overrides"]; rows.Add(cells); data.Add(new(entry.Identity, cells, cells, new Dictionary<string, object?> { ["name"] = entry.Name, ["scope"] = "User", ["state"] = entry.Disabled ? "Disabled" : "Configured" }, entry)); }
        foreach (var row in rows.Take(rows.Count - managed.Count)) data.Add(new(string.Join("|", row), row, row, new Dictionary<string, object?> { ["name"] = row[0], ["scope"] = row[3] }));
        return new InspectionTable("Startup inventory", ["Name", "Command / path", "Mechanism", "Scope / account", "Configuration"], rows, "Right-click to disable/restore current-user Run entries. MachinScope keeps the exact command/type in a recovery backup. Changes affect future logon and do not stop running processes. RunOnce, services, system entries and startup folders remain read-only. Windows startup overrides are not rewritten.", data);

    }, token);
    private readonly object adapterLock = new();
    private readonly Dictionary<string, (long Received, long Sent, long RxPackets, long TxPackets, long Tick)> adapterRates = new();
    public InspectionTable Adapters() { lock (adapterLock) return ReadAdapters(); }
    private InspectionTable ReadAdapters()
    {
        var rows = new List<string[]>(); var data = new List<InspectionRow>(); var now = Stopwatch.GetTimestamp(); var live = new HashSet<string>();
        foreach (var a in NetworkInterface.GetAllNetworkInterfaces())
        {
            live.Add(a.Id); System.Net.NetworkInformation.IPInterfaceStatistics? sampledStatistics = null;
            try { var p = a.GetIPProperties(); var s = a.GetIPStatistics(); sampledStatistics = s; IPv4InterfaceProperties? v4 = null; try { if (a.Supports(NetworkInterfaceComponent.IPv4)) v4 = p.GetIPv4Properties(); } catch (NetworkInformationException) { } rows.Add([a.Name, a.Description, a.OperationalStatus.ToString(), a.Speed.ToString(), a.GetPhysicalAddress().ToString(), string.Join("; ", p.UnicastAddresses.Select(x => $"{x.Address}/{x.PrefixLength}")), string.Join("; ", p.GatewayAddresses.Select(x => x.Address)), string.Join("; ", p.DnsAddresses), v4?.IsDhcpEnabled.ToString() ?? "Unavailable", v4?.Mtu.ToString() ?? "Unavailable", v4?.Index.ToString() ?? "Unavailable", s.BytesReceived.ToString(), s.BytesSent.ToString(), $"{s.IncomingPacketsWithErrors}/{s.OutgoingPacketsWithErrors}", $"{s.IncomingPacketsDiscarded}/{s.OutgoingPacketsDiscarded}"]); }
            catch (NetworkInformationException e) { rows.Add([a.Name, e.Message, .. Enumerable.Repeat("Unavailable", 13)]); }
            var values = rows[^1]; var extra = Enumerable.Repeat("Unavailable", 4).ToArray();
            try { var stats = sampledStatistics ?? throw new NetworkInformationException(); var rx = stats.UnicastPacketsReceived + stats.NonUnicastPacketsReceived; var tx = stats.UnicastPacketsSent + stats.NonUnicastPacketsSent;
                if (adapterRates.TryGetValue(a.Id, out var last) && a.OperationalStatus == OperationalStatus.Up) { var seconds = (now - last.Tick) / (double)Stopwatch.Frequency; extra = new[] { Calculations.Rate(stats.BytesReceived, last.Received, seconds), Calculations.Rate(stats.BytesSent, last.Sent, seconds), Calculations.Rate(rx, last.RxPackets, seconds), Calculations.Rate(tx, last.TxPackets, seconds) }.Select(v => v?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "Unavailable").ToArray(); }
                adapterRates[a.Id] = (stats.BytesReceived, stats.BytesSent, rx, tx, now);
            } catch (NetworkInformationException) { }
            var raw = values.Concat(extra).ToArray(); var cells = raw.ToArray(); if (double.TryParse(raw[3], out var speed)) cells[3] = speed > 0 ? (speed / 1_000_000).ToString("0.##") + " Mbps" : "Unavailable"; foreach (var i in new[] { 11, 12, 15, 16 }) if (double.TryParse(raw[i], out var n)) cells[i] = Calculations.Bytes(n) + (i >= 15 ? "/s" : "");
            data.Add(new(a.Id, cells, raw, new Dictionary<string, object?> { ["name"] = a.Name, ["status"] = a.OperationalStatus.ToString(), ["receive"] = extra[0], ["send"] = extra[1] }, a.Id)); rows[^1] = cells;
        }
        foreach (var id in adapterRates.Keys.Where(id => !live.Contains(id)).ToArray()) adapterRates.Remove(id);

        return new("Adapters", ["Name", "Description", "Status", "Link bps", "MAC", "Addresses/prefix", "Gateways", "DNS", "DHCP", "MTU", "Index", "Received B", "Sent B", "Errors in/out", "Discards in/out", "Receive/s", "Send/s", "Packets in/s", "Packets out/s"], rows, "Rates use elapsed monotonic time; first samples, down interfaces and reset counters are unavailable. Link speed is reported capacity, not measured throughput. Interface counters include all traffic and are not attributed to processes.", data);
    }
    public IReadOnlyList<ConnectionRow> Connections() { lock (connectionLock) return connections.Update(EndpointReader.Read(), DateTimeOffset.Now); }
    public Task<ProcessDetails> DetailsAsync(ProcessSample sample, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var snapshot = Core.Application.Services.ProcessInspection.Snapshot(sample, "Live identity has not been verified.");
        try
        {
            using var process = Validated(sample, false);
            var rows = snapshot.General.Rows.Where(r => r[0] != "Inspection status").ToList();
            var metadata = ProcessMetadata.Read(sample.Id);
            if (metadata == null || metadata.Started != sample.Started) return Core.Application.Services.ProcessInspection.Snapshot(sample, "Live metadata unavailable or identity changed. Showing selected snapshot only.");
            var path = metadata.Path;
            rows.RemoveAll(r => r[0] == "Executable"); rows.Add(["Executable", path ?? "Unavailable"]);
            if (path != null) try { var version = FileVersionInfo.GetVersionInfo(path); rows.Add(["Publisher metadata (not verified)", version.CompanyName ?? "Unknown"]); rows.Add(["Version", version.FileVersion ?? "Unknown"]); } catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException) { rows.Add(["Version metadata", e.Message]); }
            rows.Add(["Signature", "Unavailable: trust-chain verification not implemented"]);
            List<string[]> Read(Func<IEnumerable<string[]>> collect, int columns)
            {
                try { token.ThrowIfCancellationRequested(); return collect().ToList(); }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ManagementException or UnauthorizedAccessException or NotSupportedException) { return [["Unavailable: " + e.Message, .. Enumerable.Repeat("—", columns - 1)]]; }
            }
            var command = Read(() => Query("root\\cimv2", $"SELECT CommandLine FROM Win32_Process WHERE ProcessId={sample.Id}", ["CommandLine"], token), 1);
            foreach (var value in command) rows.Add(["Command line (live)", value[0]]);
            var threads = Read(() => {
                var result = new List<string[]>();
                foreach (ProcessThread t in process.Threads) using (t) { token.ThrowIfCancellationRequested(); try { result.Add([t.Id.ToString(), t.ThreadState.ToString(), t.TotalProcessorTime.ToString(), t.BasePriority.ToString()]); } catch (Win32Exception e) { result.Add([t.Id.ToString(), e.Message, "Unavailable", "Unavailable"]); } }
                return result;
            }, 4);
            var modules = Read(() => {
                var result = new List<string[]>();
                foreach (ProcessModule m in process.Modules) using (m) { token.ThrowIfCancellationRequested(); result.Add([m.ModuleName, m.FileName, $"0x{m.BaseAddress:X}", m.ModuleMemorySize.ToString()]); }
                return result;
            }, 4);
            var services = Read(() => Query("root\\cimv2", $"SELECT Name,DisplayName,State FROM Win32_Service WHERE ProcessId={sample.Id}", ["Name", "DisplayName", "State"], token), 3);
            InspectionTable endpoints;
            try { endpoints = Core.Application.Services.PresentationTables.Connections(Connections().Where(c => c.ProcessId == sample.Id && c.OwnerStarted == sample.Started)); }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or NetworkInformationException) { endpoints = new("Connections", ["Status"], [["Unavailable: " + e.Message]]); }
            using var revalidated = Validated(sample, false);
            token.ThrowIfCancellationRequested(); rows.Add(["Inspection status", "Identity verified before and after collection. Individual providers may be unavailable."]);
            return new ProcessDetails(new("General", ["Property", "Value"], rows), new("Threads", ["ID", "State", "CPU time", "Base priority"], threads), new("Modules", ["Name", "Path", "Base address", "Size B"], modules), new("Services", ["Name", "Display name", "State"], services), endpoints);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        { token.ThrowIfCancellationRequested(); return Core.Application.Services.ProcessInspection.Snapshot(sample, "Selected snapshot only: " + e.Message); }
    }, token);
    private static Process Validated(ProcessSample sample, bool modification)
    {
        if (sample.Started == null) throw new InvalidOperationException("Start time unavailable; cannot safely validate this process identity.");
        var p = Process.GetProcessById(sample.Id);
        try
        {
            if (p.StartTime != sample.Started) throw new InvalidOperationException("Process exited or PID was reused. Refresh first.");
            if (modification)
            {
                using var handle = NativeMethods.OpenProcess(0x1000, false, (uint)sample.Id);
                if (handle.IsInvalid || !NativeMethods.IsProcessCritical(handle, out var critical)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (critical || sample.Id == Environment.ProcessId || sample.Id <= 4) throw new InvalidOperationException("MachinScope refuses to modify a critical process or itself.");
                var path = p.MainModule?.FileName ?? throw new InvalidOperationException("Executable path unavailable.");
                if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("MachinScope refuses to modify executables inside Windows.");
            }
            return p;
        }
        catch { p.Dispose(); throw; }
    }
    public void EndProcess(ProcessSample sample, bool tree)
    {
        if (tree) throw new NotSupportedException("Review the frozen descendant list in MachinScope; each process must be validated individually.");
        using var handle = ModificationHandle(sample, 1); if (!NativeMethods.TerminateProcess(handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error()); logger.LogWarning("AUDIT end process PID {Pid}, started {Started}", sample.Id, sample.Started);
    }
    public void SetPriority(ProcessSample sample, string priority)
    {
        if (!Enum.TryParse<ProcessPriorityClass>(priority, out var parsed) || parsed == ProcessPriorityClass.RealTime) throw new ArgumentException("Choose Idle, BelowNormal, Normal, AboveNormal or High.");
        using var handle = ModificationHandle(sample, 0x200); if (!NativeMethods.SetPriorityClass(handle, (uint)parsed)) throw new Win32Exception(Marshal.GetLastWin32Error()); logger.LogWarning("AUDIT priority PID {Pid} set to {Priority}", sample.Id, priority);
    }
    public void SetAffinity(ProcessSample sample, ulong mask)
    {
        if (Environment.ProcessorCount > 64) throw new NotSupportedException("Processor groups above 64 CPUs are not supported.");
        var valid = Environment.ProcessorCount == 64 ? ulong.MaxValue : (1UL << Environment.ProcessorCount) - 1;
        if (mask == 0 || (mask & ~valid) != 0) throw new ArgumentException("Affinity mask must select existing processors.");
        using var handle = ModificationHandle(sample, 0x200); if (!NativeMethods.SetProcessAffinityMask(handle, (UIntPtr)mask)) throw new Win32Exception(Marshal.GetLastWin32Error()); logger.LogWarning("AUDIT affinity PID {Pid} mask {Mask}", sample.Id, mask);
    }
    private static SafeProcessHandle ModificationHandle(ProcessSample sample, uint access)
    {
        if (sample.Started == null) throw new InvalidOperationException("Start time unavailable; cannot validate process identity.");
        var handle = NativeMethods.OpenProcess(access | 0x1000, false, (uint)sample.Id);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!NativeMethods.GetProcessTimes(handle, out var created, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            long expectedCreation;
            try { expectedCreation = sample.Started.Value.ToUniversalTime().ToFileTimeUtc(); }
            catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("Invalid process start time. Refresh first."); }
            if (created != expectedCreation) throw new InvalidOperationException("Process exited or PID was reused. Refresh first.");
            if (!NativeMethods.IsProcessCritical(handle, out var critical)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (critical || sample.Id <= 4 || sample.Id == Environment.ProcessId) throw new InvalidOperationException("MachinScope refuses to modify critical processes or itself.");
            var buffer = new System.Text.StringBuilder(32768); uint length = (uint)buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (buffer.ToString().StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("MachinScope refuses to modify executables inside Windows.");
            // Validation and mutation use this same open handle, never a second PID lookup.
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
}

