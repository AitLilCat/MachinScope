using System.ComponentModel;
using System.Diagnostics;
using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.Win32;
namespace Core.Windows.Hardware;

public interface IWindowsToolRuntime
{
    string SystemDirectory { get; }
    bool Exists(string path);
    string? Edition { get; }
    Task<bool> SupportsUriAsync(Uri uri);
    Task LaunchAsync(ProcessStartInfo start);
}
public sealed class WindowsToolRuntime : IWindowsToolRuntime
{
    public string SystemDirectory => Environment.GetFolderPath(Environment.SpecialFolder.System);
    public bool Exists(string path) => File.Exists(path);
    public string? Edition {
        get { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"); return key?.GetValue("EditionID") as string; }
    }
    public async Task<bool> SupportsUriAsync(Uri uri) => await global::Windows.System.Launcher.QueryUriSupportAsync(uri, global::Windows.System.LaunchQuerySupportType.Uri) == global::Windows.System.LaunchQuerySupportStatus.Available;
    public Task LaunchAsync(ProcessStartInfo start) { Process.Start(start)?.Dispose(); return Task.CompletedTask; }
}

public sealed class WindowsAdministrationTools : IAdministrationTools, IAsyncDisposable
{
    private readonly IPlatformCapabilities platform;
    private readonly IPrivilegeService privileges;
    private readonly IWindowsToolRuntime runtime;
    private sealed record Snapshot(IReadOnlyList<ToolAvailability> Tools);
    private readonly AsyncSnapshotCache<Snapshot> cache = new(TimeSpan.FromSeconds(30));
    public WindowsAdministrationTools(IPlatformCapabilities platform, IPrivilegeService privileges, IWindowsToolRuntime? runtime = null)
    {
        if (platform.Platform.Kind != PlatformKind.Windows) throw new PlatformNotSupportedException("Windows administration providers require Windows.");
        this.platform = platform; this.privileges = privileges; this.runtime = runtime ?? new WindowsToolRuntime();
    }
    public async Task<IReadOnlyList<ToolAvailability>> InspectAsync(bool force, CancellationToken token) =>
        (await cache.ReadAsync(ct => Task.Run(async () => {
            var elevated = privileges.Current().Elevated; var result = new List<ToolAvailability>();
            foreach (var tool in AdministrationCatalog.Filter(platform.Platform.Kind)) {
                ct.ThrowIfCancellationRequested(); var probe = await Probe(tool, ct);
                result.Add(AdministrationCatalog.Availability(tool, platform.Platform.Kind, platform.Capabilities, elevated, probe.Available, probe.Reason));
            }
            return new Snapshot(result);
        }, ct), force, token)).Tools;
    private static bool EditionAllowsLocalUsers(string? edition) => edition != null &&
        (edition.StartsWith("Professional", StringComparison.OrdinalIgnoreCase) || edition.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase)
         || edition.StartsWith("Education", StringComparison.OrdinalIgnoreCase) || edition.StartsWith("Server", StringComparison.OrdinalIgnoreCase));
    private string SystemFile(string name)
    {
        if (string.IsNullOrWhiteSpace(runtime.SystemDirectory) || Path.GetFileName(name) != name) throw new InvalidOperationException("Invalid system utility registration.");
        return Path.Combine(runtime.SystemDirectory, name);
    }
    private async Task<(bool Available, string Reason)> Probe(AdministrationTool tool, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (tool.EntryPoint is not { } entry) return (true, "Opens the canonical MachinScope page.");
        try {
            if (entry.EditionRestricted && !EditionAllowsLocalUsers(runtime.Edition)) return (false, "Local Users and Groups is not supported by the detected Windows edition, or edition support could not be verified.");
            if (entry.Kind == EntryPointKind.SettingsUri) {
                var supported = await runtime.SupportsUriAsync(new(entry.Target)).WaitAsync(token); token.ThrowIfCancellationRequested();
                return (supported, supported ? "Windows URI handler is registered. Page availability can depend on Windows build, edition and policy; Windows may redirect it." : "No supported Windows URI handler is available.");
            }
            var launcher = entry.Kind == EntryPointKind.ManagementConsole ? "mmc.exe" : entry.Kind == EntryPointKind.ControlPanel ? "control.exe" : entry.Target;
            if (!runtime.Exists(SystemFile(launcher)) || !runtime.Exists(SystemFile(entry.Target))) return (false, "Required Windows utility or console is missing on this installation.");
            return (true, "Native Windows entry point is installed. Windows permissions and policy still apply.");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or Win32Exception or System.Runtime.InteropServices.COMException)
        { return (false, "Availability check failed: " + e.Message); }
    }
    public async Task<ProcessStartInfo> PrepareAsync(string id, CancellationToken token)
    {
        var tool = AdministrationCatalog.Tools.SingleOrDefault(t => t.Id == id) ?? throw new ArgumentException("Unknown administration tool.", nameof(id));
        if (tool.EntryPoint == null) throw new InvalidOperationException("This entry opens a MachinScope page rather than an external utility.");
        var probe = await Probe(tool, token);
        var availability = AdministrationCatalog.Availability(tool, platform.Platform.Kind, platform.Capabilities, privileges.Current().Elevated, probe.Available, probe.Reason);
        if (!availability.CanOpen) throw new InvalidOperationException(availability.Explanation);
        var entry = tool.EntryPoint;
        var start = new ProcessStartInfo { UseShellExecute = true, WorkingDirectory = runtime.SystemDirectory };
        switch (entry.Kind) {
            case EntryPointKind.SettingsUri: start.FileName = entry.Target; break;
            case EntryPointKind.ManagementConsole: start.FileName = SystemFile("mmc.exe"); start.ArgumentList.Add(SystemFile(entry.Target)); break;
            case EntryPointKind.ControlPanel: start.FileName = SystemFile("control.exe"); start.ArgumentList.Add(SystemFile(entry.Target)); break;
            default: start.FileName = SystemFile(entry.Target); break;
        }
        if (entry.Argument != null) start.ArgumentList.Add(entry.Argument);
        token.ThrowIfCancellationRequested(); return start;
    }
    public async Task OpenAsync(string id, CancellationToken token) {
        var start = await PrepareAsync(id, token); token.ThrowIfCancellationRequested(); await runtime.LaunchAsync(start);
    }
    public ValueTask DisposeAsync() => cache.DisposeAsync();
}
