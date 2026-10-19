using Core.Domain.Models;
namespace Core.Windows.Hardware;

public sealed partial class SystemInspector
{
    public Task<InspectionTable> ServicesAsync(CancellationToken token) => Task.Run(() =>
    {
        var source = Query(@"root\cimv2", "SELECT * FROM Win32_Service", ["Name", "DisplayName", "State", "StartMode", "ProcessId", "StartName", "PathName"], token);
        var rows = source.Select(r => new InspectionRow("service:" + r[0], r, r, new Dictionary<string, object?> { ["name"] = r[0], ["state"] = r[2], ["pid"] = r[4] })).ToArray();
        return new InspectionTable("System services", ["Name", "Display name", "State", "Start mode", "PID", "Account", "Executable"], source,
            "Read-only service inspection. Service start/stop/configuration and scheduled-task inspection are unsupported; current-user startup controls remain in Startup applications.", rows);
    }, token);
}
