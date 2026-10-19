using Core.Domain.Models;
namespace Core.Application.Interfaces;

// Compatibility facade while UI workflows move to the focused services below.
public interface ISystemInspector : IProcessInspector, IProcessController, IPerformanceCollector, IStorageInspector, INetworkInspector, IStartupManager, IConnectionTimeline, IServiceInspector
{
    Task<InspectionTable> OverviewAsync(CancellationToken token);
}
public interface IFileInspector
{
    InspectionTable List(string path, string filter, CancellationToken token);
    Task<ScanReport> ScanAsync(string root, bool includeHidden, IProgress<string>? progress, CancellationToken token);
    Task<DuplicateReport> DuplicatesAsync(string root, IProgress<string>? progress, CancellationToken token);
    void Copy(string source, string destination);
    void Move(string source, string destination);
    void Recycle(string path);
}
public interface IPreferencesStore { Preferences Load(); void Save(Preferences preferences); string DataDirectory { get; } }
public interface INetworkDiagnostics { Task<string> RunAsync(string kind, string host, int port, CancellationToken token); }
