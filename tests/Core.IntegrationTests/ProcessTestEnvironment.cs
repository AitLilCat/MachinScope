using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Windows.Hardware;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.IntegrationTests;

internal static class ProcessTestEnvironment
{
    internal const string StandardUserCategory = "StandardUserProcessLaunch";
    internal const string LaunchDeniedMessage = "Managed launch requires a standard, non-elevated MachinScope session.";

    // Read the real provider's current-process observation. Never substitute a Medium
    // context for an elevated test host or pass a test override into production launch.
    internal static string Integrity() => new SystemInspector(NullLogger<SystemInspector>.Instance)
        .Processes().Single(p => p.Id == Environment.ProcessId).Integrity ?? "Unavailable";

    internal static string ContextJson()
    {
        var integrity = Integrity();
        return JsonSerializer.Serialize(new { integrity, canRunStandardUserProcessLaunch = integrity == "Medium" });
    }

    internal static void RequireStandardUser()
    {
        var integrity = Integrity();
        if (integrity != "Medium") Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(
            $"{StandardUserCategory} requires a real Medium-integrity test host; observed {integrity}. No managed launch/restart was executed.");
    }

    internal static string DotnetHost => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));

    // A disposable test-owned process, independent of the app's managed-launch feature.
    // The fixture deliberately inherits the test host's token; no elevation/demotion occurs.
    internal static async Task<Process> StartOwnedFixture()
    {
        var start = new ProcessStartInfo(DotnetHost) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(ProcessTestEnvironment).Assembly.Location);
        start.ArgumentList.Add("--process-fixture");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture launch failed.");
        try {
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (ready != "CORE control fixture ready") throw new InvalidOperationException("Fixture did not report readiness.");
            return process;
        }
        catch { await StopOwnedFixture(process); process.Dispose(); throw; }
    }

    internal static async Task StopOwnedFixture(Process process)
    {
        if (!process.HasExited) process.Kill();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
