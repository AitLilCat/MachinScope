using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Core.Application.Interfaces;
using Core.Domain.Models;
using Microsoft.Extensions.Logging;
namespace Core.Desktop.ViewModels;
public sealed class ShellViewModel(ISystemInspector inspector, IFileInspector files, INetworkDiagnostics diagnostics, IPreferencesStore store, ILogger<ShellViewModel> logger, IFilePropertiesInspector fileProperties, IDiskInspector disks, IDesktopIntegration desktop, IPlatformCapabilities capabilities, IPrivilegeService privileges, Core.Application.Services.ActionService actions, IDeviceInspector devices, IStorageSnapshots storage, Core.Application.Services.IAdministrationTools administration) : INotifyPropertyChanged
{
    public Core.Application.Services.IAdministrationTools Administration { get; } = administration;
    public IStorageSnapshots Storage { get; } = storage;
    public IDeviceInspector Devices { get; } = devices;
    public IFilePropertiesInspector FileProperties { get; } = fileProperties;
    public IDiskInspector Disks { get; } = disks;
    public IDesktopIntegration Desktop { get; } = desktop;
    public IPlatformCapabilities Capabilities { get; } = capabilities;
    public IPrivilegeService Privileges { get; } = privileges;
    public Core.Application.Services.ActionService Actions { get; } = actions;
    public ISystemInspector Inspector { get; } = inspector;
    public IFileInspector Files { get; } = files;
    public INetworkDiagnostics Diagnostics { get; } = diagnostics;
    public IPreferencesStore Store { get; } = store;
    public Preferences Preferences { get; private set; } = store.Load();
    public Core.Application.Services.LocalDiagnostics Measurements { get; } = new();
    public IReadOnlyList<ProcessSample> Processes { get; private set; } = [];
    public InspectionTable Table { get; private set; } = new("", [], []);
    public SystemSample? LastSample { get; private set; }
    public RollingBuffer<SystemSample> History { get; } = new(900);
    private string summary = "Sampling…", status = "Ready";
    public string Summary { get => summary; private set { summary = value; Notify(); } }
    public string Status { get => status; set { status = value; Notify(); } }
        public bool Tree { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public async Task<(SystemSample Sample, IReadOnlyList<ProcessSample>? Processes)> CollectAsync(bool processes, CancellationToken token) => await Task.Run(() => {
        token.ThrowIfCancellationRequested(); var sample = Inspector.Sample();
        if (!processes) return (sample, (IReadOnlyList<ProcessSample>?)null);
        var started = Measurements.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try { var snapshot = Inspector.Processes(); if (started != 0) Measurements.Record(Core.Application.Services.DiagnosticMetric.ProcessSnapshot, System.Diagnostics.Stopwatch.GetElapsedTime(started), Core.Application.Services.RefreshState.Applied); return (sample, (IReadOnlyList<ProcessSample>?)snapshot); }
        catch { if (started != 0) Measurements.Record(Core.Application.Services.DiagnosticMetric.ProcessSnapshot, System.Diagnostics.Stopwatch.GetElapsedTime(started), Core.Application.Services.RefreshState.Failed); throw; }
    }, token);
    public void Update((SystemSample Sample, IReadOnlyList<ProcessSample>? Processes) update, bool publishProcesses = true)
    {
        var s = update.Sample; LastSample = s; History.Add(s); Summary = $"CPU {(s.CpuPercent is { } c ? $"{c:0.0}%" : "Sampling")}   RAM {Calculations.Bytes(s.TotalMemory - s.AvailableMemory)} / {Calculations.Bytes(s.TotalMemory)}   Processes {s.ProcessCount}   Up {s.Uptime:dd\\.hh\\:mm\\:ss}   ↓ {Calculations.Bytes(s.ReceiveRate)}/s   ↑ {Calculations.Bytes(s.SendRate)}/s";
        if (update.Processes != null) { Processes = update.Processes; if (publishProcesses) ShowProcesses(); }
    }
    public void SetProcesses(IReadOnlyList<ProcessSample> samples, bool publish = true) { Processes = samples; if (publish) ShowProcesses(); }
    public void ShowProcesses()
    {
        var processes = Processes.AsEnumerable();
        if (Tree)
        {
            var visited = new HashSet<int>(); var ordered = new List<ProcessSample>(); var children = Processes.GroupBy(p => p.ParentId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Name).ToArray());
            void Visit(ProcessSample p, int depth) { if (!visited.Add(p.Id)) return; ordered.Add(p with { Name = new string(' ', Math.Min(depth, 20) * 2) + p.Name }); if (children.TryGetValue(p.Id, out var child)) foreach (var q in child) Visit(q, depth + 1); }
            var ids = Processes.Select(p => p.Id).ToHashSet(); foreach (var p in Processes.Where(p => !ids.Contains(p.ParentId) || p.Id == p.ParentId)) Visit(p, 0); foreach (var p in Processes) Visit(p, 0); processes = ordered;
        }
        SetTable(Core.Application.Services.PresentationTables.Processes(processes));
    }
    public void SetTable(InspectionTable table) { Table = table; Notify(nameof(Table)); }
    public void SavePreferences(Preferences preferences) { Store.Save(preferences); Preferences = preferences; Measurements.Enabled = preferences.LocalDiagnostics; }
    public void Error(Exception error) { var classified = Core.Application.Services.OperationErrors.Classify(error); if (error is not OperationCanceledException) logger.LogError(error, "Operation failed ({Category})", classified.Category); Status = classified.Message + (error is OperationCanceledException ? "" : " " + error.Message); }
}

