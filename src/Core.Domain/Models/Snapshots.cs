namespace Core.Domain.Models;

public sealed record SystemSample(DateTimeOffset Time, double? CpuPercent, ulong TotalMemory, ulong AvailableMemory, ulong CommitUsed, ulong CommitLimit, int ProcessCount, TimeSpan Uptime, double ReceiveRate, double SendRate);
public sealed record ProcessSample(int Id, int ParentId, string Name, DateTime? Started, string? Path, double? CpuPercent, long? WorkingSet, long? PrivateBytes, int? Threads, int? Handles, string Priority, double? ReadRate, double? WriteRate, string? Error)
{
    public string Identity => $"{Id}:{Started?.ToUniversalTime().Ticks}";
    public string? User { get; init; }
    public string? Architecture { get; init; }
    public string? Integrity { get; init; }
    public string? CommandLine { get; init; }
    public TimeSpan? CpuTime { get; init; }
    public string State { get; init; } = "Running (observed)";
}
public sealed record InspectionTable(string Title, string[] Columns, IReadOnlyList<string[]> Rows, string? Notice = null, IReadOnlyList<InspectionRow>? Data = null);
public sealed record InspectionRow(string Key, string[] Cells, string[] Raw, IReadOnlyDictionary<string, object?> Fields, object? Subject = null);
public sealed record ColumnLayout(string Name, double Width, bool Visible, int Order);
public sealed record SortColumn(int Index, bool Descending);
public sealed record ConnectionRow(string Protocol, string LocalAddress, int LocalPort, string RemoteAddress, int? RemotePort, string State, int ProcessId, string ProcessName)
{
    public long? NativeCreated { get; init; }
    public DateTime? OwnerStarted { get; init; }
    public string? Executable { get; init; }
    public string? User { get; init; }
    public string Direction { get; init; } = "Unknown";
    public string? ObservationId { get; init; }
    public DateTimeOffset? FirstObserved { get; init; }
    public string EndpointKey => $"{Protocol}|{LocalAddress}|{LocalPort}|{RemoteAddress}|{RemotePort}|{ProcessId}|{OwnerStarted?.ToUniversalTime().Ticks}|{NativeCreated}";
}
public sealed record ScanIssue(string Path, string Message);
public sealed record SizedPath(string Path, long Bytes);
public sealed record ScanReport(string Root, long Bytes, long Files, long Folders, int MaxDepth, long ReparsePointsSkipped, long IssueCount, IReadOnlyList<ScanIssue> Issues, IReadOnlyList<SizedPath> LargestFiles, IReadOnlyList<SizedPath> LargestDirectories, IReadOnlyDictionary<string, long> Extensions, TimeSpan Duration);
public sealed record DuplicateGroup(long BytesPerFile, IReadOnlyList<string> Paths);
public sealed record DuplicateReport(IReadOnlyList<DuplicateGroup> Groups, long ReclaimableBytes, long HardLinksSkipped, long Errors, bool Truncated);
public sealed record ProcessDetails(InspectionTable General, InspectionTable Threads, InspectionTable Modules, InspectionTable Services, InspectionTable Connections);
public sealed record Preferences(string Theme = "System", int RefreshSeconds = 2, string? ExportDirectory = null, string LogLevel = "Information", int LogRetentionDays = 7, bool IncludeHiddenFiles = true)
{
    public NavigationPreferences Navigation { get; init; } = new();
    public Dictionary<string, ColumnLayout[]> Tables { get; init; } = new();
    public bool StartupControls { get; init; } = true;
    public bool PauseWhenInactive { get; init; } = true;
    public int HistorySeconds { get; init; } = 60;
    public bool LocalDiagnostics { get; init; }
}
public sealed record NavigationPreferences(string LastDestination = "core.overview", string[]? ExpandedCategories = null, bool PaneOpen = true);
public sealed record LaunchSpec(string Executable, string[] Arguments, string WorkingDirectory);
public sealed record RestartCapability(bool Available, string Reason);

public sealed record StartupEntry(string Name, string Command, string RegistryPath, int Kind, bool Disabled) { public string Identity => $"HKCU|64|{RegistryPath}|{Name}|{Disabled}"; }
