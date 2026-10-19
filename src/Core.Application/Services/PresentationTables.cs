using System.Globalization;
using Core.Domain.Models;
namespace Core.Application.Services;

public static class PresentationTables
{
    private static string Raw(object? v) => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "Unavailable";
    public static InspectionTable Processes(IEnumerable<ProcessSample> samples)
    {
        string[] columns = ["Name", "PID", "CPU %", "Memory", "Disk read/s", "Disk write/s", "Threads", "Parent PID", "Private memory", "Handles", "Priority", "Started", "User", "Executable", "Command line", "Architecture", "Integrity", "CPU time", "State", "Access"];
        var rows = samples.Select(p => {
            string[] raw = [p.Name, Raw(p.Id), Raw(p.CpuPercent), Raw(p.WorkingSet), Raw(p.ReadRate), Raw(p.WriteRate), Raw(p.Threads), Raw(p.ParentId), Raw(p.PrivateBytes), Raw(p.Handles), p.Priority, p.Started?.ToString("O") ?? "Unavailable", p.User ?? "Unavailable", p.Path ?? "Unavailable", p.CommandLine ?? "Inspect in Details", p.Architecture ?? "Unavailable", p.Integrity ?? "Unavailable", p.CpuTime?.ToString() ?? "Unavailable", p.State, p.Error ?? "Readable"];
            var cells = raw.ToArray(); cells[2] = p.CpuPercent?.ToString("0.00", CultureInfo.InvariantCulture) ?? "Sampling";
            foreach (var i in new[] { 3, 4, 5, 8 }) if (double.TryParse(raw[i], CultureInfo.InvariantCulture, out var n)) cells[i] = Calculations.Bytes(n) + (i is 4 or 5 ? "/s" : "");
            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["name"] = p.Name, ["process"] = p.Name, ["pid"] = p.Id, ["cpu"] = p.CpuPercent, ["memory"] = p.WorkingSet, ["user"] = p.User, ["path"] = p.Path, ["state"] = p.State, ["active"] = p.CpuPercent > 0 || p.ReadRate > 0 || p.WriteRate > 0 ? "yes" : "no" };
            return new InspectionRow(p.Started.HasValue ? p.Identity : $"unverified:{p.Id}:{p.Name}", cells, raw, fields, p);
        }).ToArray();
        return new("Processes", columns, rows.Select(r => r.Cells).ToArray(), "CPU is normalized across logical processors. First rates are unavailable. Network bytes cannot be attributed reliably. Unreadable identities cannot be controlled. Ctrl+A selects visible rows; Ctrl+C copies; Shift-click extends selection.", rows);
    }
    public static InspectionTable Connections(IEnumerable<ConnectionRow> samples)
    {
        string[] columns = ["Process", "PID", "Protocol", "Local address", "Local port", "Remote address", "Remote port", "State", "Direction", "Observed since", "Executable", "User"];
        var rows = samples.Select(c => {
            string[] values = [c.ProcessName, Raw(c.ProcessId), c.Protocol, c.LocalAddress, Raw(c.LocalPort), c.RemoteAddress, Raw(c.RemotePort), c.State, c.Direction, c.FirstObserved?.ToString("O") ?? "Unavailable", c.Executable ?? "Unavailable", c.User ?? "Unavailable"];
            return new InspectionRow(c.ObservationId ?? Guid.NewGuid().ToString(), values, values, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["process"] = c.ProcessName, ["pid"] = c.ProcessId, ["protocol"] = c.Protocol, ["local"] = c.LocalAddress, ["remote"] = c.RemoteAddress, ["localport"] = c.LocalPort, ["remoteport"] = c.RemotePort, ["state"] = c.State, ["direction"] = c.Direction }, c);
        }).ToArray();
        return new("Connections", columns, rows.Select(r => r.Cells).ToArray(), "Direction is Unknown: IP Helper tables do not identify the initiator. Observed since means first seen by MachinScope, not connection creation. Native TCP bind timestamps distinguish reuse where provided; fallback observations can miss close/reopen between samples. UDP has no remote peer. Per-connection bytes and safe connection close are unavailable.", rows);
    }
    public static InspectionTable Storage(InspectionTable source)
    {
        var rows = source.Rows.Select(r => { var cells = r.ToArray(); if (ulong.TryParse(r.ElementAtOrDefault(5), out var size)) cells[5] = Calculations.Bytes(size); return new InspectionRow(source.Title + "|" + r[0] + "|" + r[1], cells, r, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["kind"] = r[0], ["size"] = r.ElementAtOrDefault(5), ["name"] = r.ElementAtOrDefault(2) }); }).ToArray();
        return source with { Rows = rows.Select(r => r.Cells).ToArray(), Data = rows };
    }
}
