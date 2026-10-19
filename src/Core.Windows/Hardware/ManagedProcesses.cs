using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Core.Domain.Models;
using Core.Windows.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
namespace Core.Windows.Hardware;
public sealed partial class SystemInspector
{
    private readonly Dictionary<string, (LaunchSpec Spec, string Hash)> launches = new();
    private readonly object launchLock = new();
    public ProcessSample LaunchManaged(LaunchSpec spec)
    {
        if (ProcessMetadata.Read(Environment.ProcessId)?.Integrity != "Medium") throw new InvalidOperationException("Managed launch requires a standard, non-elevated MachinScope session.");
        var executable = Path.GetFullPath(spec.Executable); var directory = Path.GetFullPath(spec.WorkingDirectory);
        if (!Path.IsPathFullyQualified(spec.Executable) || !File.Exists(executable) || !Directory.Exists(directory)) throw new ArgumentException("Choose an existing absolute executable and working directory.");
        if (executable.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Managed launch excludes Windows executables.");
        using var file = File.OpenRead(executable); var hash = Convert.ToHexString(SHA256.HashData(file));
        var recipe = new LaunchSpec(executable, spec.Arguments.ToArray(), directory);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = directory }; foreach (var a in recipe.Arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Launch failed.");
        var sample = new ProcessSample(process.Id, Environment.ProcessId, Path.GetFileNameWithoutExtension(executable), process.StartTime, executable, null, null, null, null, null, "Unavailable", null, null, null);
        lock (launchLock) { if (launches.Count >= 1024) launches.Clear(); launches[sample.Identity] = (recipe, hash); }
        logger.LogWarning("AUDIT managed launch PID {Pid}, started {Started}, executable {Executable}", sample.Id, sample.Started, executable); return sample;
    }
    public RestartCapability CanRestart(ProcessSample sample)
    {
        lock (launchLock) return launches.ContainsKey(sample.Identity) ? new(true, "MachinScope recorded executable, arguments and working directory in this session; identity and hash will be revalidated.") : new(false, "Launch context unknown. Restart is supported only for standard-user processes launched through MachinScope in this session. Services and elevated processes are excluded.");
    }
    public ProcessSample RestartProcess(ProcessSample sample)
    {
        (LaunchSpec Spec, string Hash) launch; lock (launchLock) if (!launches.TryGetValue(sample.Identity, out launch)) throw new InvalidOperationException(CanRestart(sample).Reason);
        using var file = File.OpenRead(launch.Spec.Executable); if (Convert.ToHexString(SHA256.HashData(file)) != launch.Hash) throw new InvalidOperationException("Executable changed since launch. Restart refused.");
        var metadata = ProcessMetadata.Read(sample.Id); if (metadata == null || metadata.Started != sample.Started || metadata.Integrity != "Medium" || !string.Equals(metadata.Path, launch.Spec.Executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Launch identity or security context changed. Restart refused.");
        using var handle = ModificationHandle(sample, 1 | 0x100000);
        if (!NativeMethods.TerminateProcess(handle, 1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (WaitForSingleObject(handle, 10000) != 0) throw new InvalidOperationException("Original process did not finish within 10 seconds; replacement was not launched.");
        lock (launchLock) launches.Remove(sample.Identity);
        try { var replacement = LaunchManaged(launch.Spec); logger.LogWarning("AUDIT restart PID {OldPid} to {NewPid}", sample.Id, replacement.Id); return replacement; }
        catch (Exception e) { throw new InvalidOperationException("Original process ended, but replacement launch failed: " + e.Message, e); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
