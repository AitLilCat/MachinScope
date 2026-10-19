using Core.Domain.Models;
namespace Core.Application.Services;

public static class ProcessInspection
{
    // A denied or stale live query must not prevent inspecting the original selection,
    // or silently attach data from another process which reused its PID.
    public static ProcessDetails Snapshot(ProcessSample sample, string reason)
    {
        InspectionTable Unavailable(string name) => new(name, ["Status"], [[reason]]);
        return new(new("General", ["Property", "Value"], [
            ["Name", sample.Name], ["PID", sample.Id.ToString()], ["Parent PID", sample.ParentId.ToString()],
            ["Started", sample.Started?.ToString("O") ?? "Unavailable"], ["Inspection status", reason],
            ["Executable", sample.Path ?? "Unavailable"], ["Command line (snapshot)", sample.CommandLine ?? "Unavailable"],
            ["User", sample.User ?? "Unavailable"], ["Integrity", sample.Integrity ?? "Unavailable"],
            ["Architecture", sample.Architecture ?? "Unavailable"], ["CPU time", sample.CpuTime?.ToString() ?? "Unavailable"],
            ["Access", sample.Error ?? "Snapshot captured; live access not guaranteed"],
            ["Network bytes/sec", "Unavailable: reliable process attribution not installed"]
        ]), Unavailable("Threads"), Unavailable("Modules"), Unavailable("Services"), Unavailable("Connections"));
    }
}
