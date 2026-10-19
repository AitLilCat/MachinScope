using System.Net;
using Core.Domain.Models;
namespace Core.Application.Services;

public static class NetworkInspection
{
    public static InspectionTable Properties(ConnectionRow c) => new("Connection", ["Property", "Value"], [
        ["Protocol", c.Protocol], ["Local address", c.LocalAddress], ["Local port", c.LocalPort.ToString()],
        ["Remote address", c.RemotePort.HasValue ? c.RemoteAddress : "Unavailable: UDP snapshot has no remote peer"],
        ["Remote port", c.RemotePort?.ToString() ?? "Unavailable"], ["State (snapshot)", c.State],
        ["Direction", c.Direction == "Unknown" ? "Unknown: snapshot provider cannot determine the initiator" : c.Direction],
        ["Owner", c.ProcessName], ["PID", c.ProcessId.ToString()],
        ["Owner started", c.OwnerStarted?.ToString("O") ?? "Unavailable: live owner cannot be verified"],
        ["Executable", c.Executable ?? "Unavailable"], ["User", c.User ?? "Unavailable"],
        ["First observed by MachinScope", c.FirstObserved?.ToString("O") ?? "Unavailable"],
        ["Native bind timestamp (FILETIME)", c.NativeCreated?.ToString() ?? "Unavailable"],
        ["Observation ID", c.ObservationId ?? "Unavailable"], ["Endpoint identity", c.EndpointKey],
        ["Traffic / purpose", "Per-connection bytes and application intent are unavailable from endpoint snapshots."]
    ]);

    public static IReadOnlyList<ConnectionRow> ForAdapter(InspectionRow adapter, IEnumerable<ConnectionRow> connections)
    {
        var addresses = (adapter.Raw.ElementAtOrDefault(5) ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => IPAddress.TryParse(value.Split('/')[0], out var address) ? address : null).Where(a => a != null).ToHashSet();
        // Wildcard listeners cannot be attributed to one adapter from these snapshots.
        return connections.Where(c => IPAddress.TryParse(c.LocalAddress, out var address) && addresses.Contains(address)).ToArray();
    }
}
