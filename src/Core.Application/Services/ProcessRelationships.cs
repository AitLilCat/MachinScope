using Core.Domain.Models;
namespace Core.Application.Services;

public static class ProcessRelationships
{
    public static ProcessSample? Parent(ProcessSample child, IEnumerable<ProcessSample> snapshot) => child.Started == null ? null :
        snapshot.FirstOrDefault(p => p.Id == child.ParentId && p.Id != child.Id && p.Started != null && p.Started <= child.Started);
    public static ProcessSample Owner(ConnectionRow connection, IEnumerable<ProcessSample> snapshot)
    {
        var observed = connection.OwnerStarted == null ? null : snapshot.FirstOrDefault(p => p.Id == connection.ProcessId && p.Started == connection.OwnerStarted);
        return observed ?? new ProcessSample(connection.ProcessId, 0, connection.ProcessName, connection.OwnerStarted,
            connection.Executable, null, null, null, null, null, "Unavailable", null, null,
            "Owner fields captured from the selected connection; live identity is verified on demand.") { User = connection.User };
    }
}
