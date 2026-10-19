using Core.Domain.Models;
namespace Core.Application.Services;
public static class ConnectionTimelineTable
{
    public static InspectionTable Create(IEnumerable<ConnectionEvent> events)
    {
        var data = events.Select(e =>
        {
            var c = e.Connection;
            string[] cells = [e.Time.ToString("O"), e.Kind.ToString(), c.ProcessName, c.ProcessId.ToString(), c.Protocol, $"{c.LocalAddress}:{c.LocalPort}", $"{c.RemoteAddress}:{c.RemotePort}", e.PreviousState == null ? c.State : e.PreviousState + " → " + c.State, c.ObservationId!, c.OwnerStarted?.ToString("O") ?? "Unavailable"];
            return new InspectionRow(e.Sequence.ToString(), cells, cells, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["process"] = c.ProcessName, ["pid"] = c.ProcessId, ["protocol"] = c.Protocol, ["state"] = c.State, ["event"] = e.Kind.ToString() }, c);
        }).ToArray();
        return new("Connection timeline", ["Observed time", "Change", "Process", "PID", "Protocol", "Local", "Remote", "State", "Observation ID", "Owner started"], data.Select(r => r.Cells).ToArray(), "Snapshot observations, capped at 2,048 events. Changes between polls can be missed. Appearance is first observed, not socket creation. Untrackable identities are excluded.", data);
    }
}
