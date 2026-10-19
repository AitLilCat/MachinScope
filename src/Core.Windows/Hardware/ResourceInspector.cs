using System.Management;
using Core.Domain.Models;
namespace Core.Windows.Hardware;
public sealed partial class SystemInspector
{
    public Task<InspectionTable> ResourceAsync(string resource, CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<string[]>();
        void Metrics(string scope, string query, string[] fields, string[] labels, string[] units)
        {
            foreach (var values in Query(scope, query, fields, token)) for (var i = 0; i < fields.Length; i++) rows.Add([labels[i], values[i], units[i]]);
        }
        try
        {
            if (resource == "CPU")
            {
                foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation", ["Name", "PercentProcessorTime", "PercentPrivilegedTime", "PercentUserTime", "ProcessorFrequency", "InterruptsPersec"], token)) { rows.Add([$"CPU {r[0]} utilization", r[1], "%"]); rows.Add([$"CPU {r[0]} kernel", r[2], "%"]); rows.Add([$"CPU {r[0]} user", r[3], "%"]); rows.Add([$"CPU {r[0]} frequency", r[4], "MHz (provider)"]); rows.Add([$"CPU {r[0]} interrupts", r[5], "/s"]); }
                Metrics("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_PerfOS_System", ["ContextSwitchesPersec", "Processes", "Threads", "ProcessorQueueLength", "SystemUpTime"], ["Context switches", "Processes", "Threads", "Processor queue", "Uptime"], ["/s", "count", "count", "count", "seconds"]);
            }
            else if (resource == "Memory") Metrics("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_PerfOS_Memory", ["AvailableBytes", "CommittedBytes", "CommitLimit", "CacheBytes", "PoolPagedBytes", "PoolNonpagedBytes", "PageReadsPersec", "PagesInputPersec", "PageFaultsPersec"], ["Available physical", "Committed", "Commit limit", "System cache", "Paged pool", "Nonpaged pool", "Disk reads for hard faults", "Pages read for hard faults", "Page faults (soft and hard)"], ["B", "B", "B", "B", "B", "B", "reads/s", "pages/s", "faults/s"]);
            else if (resource == "GPU")
            {
                foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_VideoController", ["Name", "DriverVersion", "VideoProcessor"], token)) { rows.Add(["Adapter", r[0], "name"]); rows.Add(["Driver", r[1], "version"]); rows.Add(["Processor", r[2], "name"]); }
                try { foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine", ["Name", "UtilizationPercentage"], token)) rows.Add([r[0], r[1], "% per engine instance"]); } catch (ManagementException) { rows.Add(["Engine utilization", "Unavailable", "WDDM performance provider absent"]); }
                try { foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory", ["Name", "DedicatedUsage", "SharedUsage"], token)) { rows.Add([r[0] + " dedicated usage", r[1], "B"]); rows.Add([r[0] + " shared usage", r[2], "B"]); } } catch (ManagementException) { rows.Add(["GPU memory", "Unavailable", "Adapter memory provider absent"]); }
                rows.Add(["Temperature / power", "Unavailable", "vendor telemetry provider absent"]);
            }
            else if (resource.StartsWith("Disk "))
            {
                var disk = resource[5..];
                foreach (var r in Query("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk", ["Name", "PercentIdleTime", "DiskReadBytesPersec", "DiskWriteBytesPersec", "DiskReadsPersec", "DiskWritesPersec", "CurrentDiskQueueLength"], token).Where(r => r[0].Split(' ')[0] == disk)) { rows.Add(["Active time", double.TryParse(r[1], out var idle) ? Math.Clamp(100 - idle, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture) : "Unavailable", "% from idle time"]); for (var i = 2; i < r.Length; i++) rows.Add([new[] { "", "", "Read throughput", "Write throughput", "Read operations", "Write operations", "Queue length" }[i], r[i], i < 4 ? "B/s" : i < 6 ? "IOPS" : "count"]); }
                rows.Add(["Read/write latency", "Unavailable", "fractional PDH latency provider not installed"]);
            }
            else if (resource.StartsWith("Network")) { var adapters = Adapters(); if (resource.Length == 7) return adapters; var id = resource[8..]; var selected = adapters.Data!.Where(r => (string?)r.Subject == id).ToArray(); return adapters with { Title = "Interface performance", Rows = selected.Select(r => r.Cells).ToArray(), Data = selected }; }
        }
        catch (ManagementException e) { rows.Add(["Provider", "Unavailable", e.Message]); }
        if (rows.Count == 0) rows.Add(["Provider", "Unavailable", "No counters returned"]);
        var data = rows.Select((r, i) => new InspectionRow(resource + "|" + r[0] + "|" + i, r, r, new Dictionary<string, object?> { ["name"] = r[0], ["value"] = r[1] })).ToArray();
        return new InspectionTable(resource + " performance", ["Metric", "Value", "Unit / meaning"], rows, "Live counters are queried only on the active Performance page. Provider counters may be unavailable. Hard-fault disk reads are not the total hard-fault count. GPU engine instances are not summed into a misleading aggregate.", data);
    }, token);
}
