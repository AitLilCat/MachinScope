using Core.Domain.Models;
namespace Core.Application.Interfaces;

public interface IPlatformInformation { PlatformIdentity Platform { get; } }
public interface IPlatformCapabilities : IPlatformInformation { IReadOnlyList<Capability> Capabilities { get; } }
public interface IPrivilegeService { PrivilegeSnapshot Current(); }
public interface IProcessInspector { IReadOnlyList<ProcessSample> Processes(); Task<ProcessDetails> DetailsAsync(ProcessSample process, CancellationToken token); }
public interface IProcessController
{
    ProcessSample LaunchManaged(LaunchSpec spec);
    RestartCapability CanRestart(ProcessSample sample);
    ProcessSample RestartProcess(ProcessSample sample);
    void EndProcess(ProcessSample process, bool tree);
    void SetPriority(ProcessSample process, string priority);
    void SetAffinity(ProcessSample process, ulong mask);
}
public interface IPerformanceCollector { SystemSample Sample(); Task<InspectionTable> PerformanceAsync(CancellationToken token); Task<InspectionTable> ResourceAsync(string resource, CancellationToken token); }
public interface INetworkInspector { InspectionTable Adapters(); IReadOnlyList<ConnectionRow> Connections(); InspectionTable DnsConfiguration(); }
public interface IConnectionTimeline { IReadOnlyList<ConnectionEvent> ConnectionEvents(); }
public interface IStartupManager { Task<InspectionTable> StartupAsync(CancellationToken token); void SetStartupEnabled(StartupEntry entry, bool enabled); }
public interface IServiceInspector { Task<InspectionTable> ServicesAsync(CancellationToken token); }
public sealed record StorageSnapshot(DiskInventory Inventory, InspectionTable Table, DateTimeOffset CapturedAt);
public interface IStorageSnapshots { StorageSnapshot? Last { get; } Task<StorageSnapshot> ReadAsync(bool force, CancellationToken token); }
public interface IStorageInspector { Task<InspectionTable> StorageAsync(CancellationToken token); }
public interface IDiskInspector
{
    Task<DiskInventory> InventoryAsync(CancellationToken token);
    Task<DiskDetails> DetailsAsync(DiskTarget target, CancellationToken token);
}
public interface IDesktopIntegration
{
    void Open(string path);
    void ShowInFolder(string path);
    void Properties(string path);
    void OpenDiskUtility();
    InspectionTable FileUsers(string path);
}
public interface IFilePropertiesInspector
{
    Task<FileDetails> DetailsAsync(string path, CancellationToken token);
    Task<string> HashAsync(FileTarget target, string algorithm, CancellationToken token);
}
public interface ICoreAction<in T>
{
    string Name { get; }
    ActionRisk Risk { get; }
    string Identity(T target);
    ActionDecision Validate(T target);
    Task<string> ExecuteAsync(T target, CancellationToken token);
}
