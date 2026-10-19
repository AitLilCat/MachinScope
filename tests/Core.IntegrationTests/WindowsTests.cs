using System.Diagnostics;
using Core.Windows.Hardware;
using Core.Windows.FileSystem;
using Core.Windows.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;
[TestClass] public class WindowsTests
{
    private static SystemInspector Inspector() => new(NullLogger<SystemInspector>.Instance);
    [TestMethod] public async Task RealMemoryCpuAndProcesses()
    {
        var i = Inspector(); var first = i.Sample(); Assert.True(first.TotalMemory > 0); Assert.True(first.AvailableMemory <= first.TotalMemory); Assert.Null(first.CpuPercent); await Task.Delay(150); var second = i.Sample(); Assert.InRange(second.CpuPercent!.Value, 0, 100); var processes = i.Processes(); Assert.Contains(processes, p => p.Id == Environment.ProcessId); Assert.True(processes.Count > 10); await Task.Delay(100); Assert.Contains(i.Processes(), p => p.CpuPercent.HasValue);
    }
    [TestMethod] public void RealAdapterAndEndpointTables() { var i = Inspector(); Assert.NotEmpty(i.Adapters().Rows); Assert.NotEmpty(i.Connections()); }
    [TestMethod] public async Task RealStorageSeparatesDisksPartitionsAndVolumes()
    {
        var t = await Inspector().StorageAsync(default); Assert.Contains(t.Rows, r => r[0] == "Physical disk"); Assert.Contains(t.Rows, r => r[0] == "Partition"); Assert.Contains(t.Rows, r => r[0] == "Volume"); Assert.Contains(t.Rows, r => r[0] == "Partition → volume"); Assert.All(t.Rows, r => Assert.Equal(t.Columns.Length, r.Length));
    }
    [TestMethod] public void RefusesSelfAndStaleProcessControls()
    {
        var i = Inspector(); var self = i.Processes().Single(p => p.Id == Environment.ProcessId); Assert.Throws<InvalidOperationException>(() => i.EndProcess(self, false)); Assert.Throws<InvalidOperationException>(() => i.SetPriority(self with { Started = DateTime.MinValue }, "Normal"));
    }
    [TestMethod] public async Task ProcessDetailsUseLiveProcess()
    {
        var i = Inspector(); var self = i.Processes().Single(p => p.Id == Environment.ProcessId); var d = await i.DetailsAsync(self, default); Assert.Contains(d.General.Rows, r => r[0] == "Executable" && File.Exists(r[1])); Assert.NotEmpty(d.Threads.Rows); Assert.NotEmpty(d.Modules.Rows);
    }
    [TestMethod] public async Task LocalDiagnosticsOnly() { var d = new NetworkDiagnostics(); Assert.Contains("127.0.0.1", await d.RunAsync("DNS", "localhost", 80, default)); }
    [TestMethod, TestCategory(ProcessTestEnvironment.StandardUserCategory)] public async Task ManagedRestartCreatesNewIdentityAndRejectsUnknownContext()
    {
        ProcessTestEnvironment.RequireStandardUser();
        var inspector = Inspector(); var host = ProcessTestEnvironment.DotnetHost; Core.Domain.Models.ProcessSample? child = null;
        try { child = inspector.LaunchManaged(new(host, [typeof(WindowsTests).Assembly.Location, "--process-fixture"], AppContext.BaseDirectory)); Assert.True(inspector.CanRestart(child).Available); Assert.False(inspector.CanRestart(child with { Started = child.Started?.AddSeconds(1) }).Available); var previous = child; child = inspector.RestartProcess(child); Assert.NotEqual(previous.Identity, child.Identity); Assert.False(inspector.CanRestart(previous).Available); await Task.Delay(200); Assert.Contains(inspector.Processes(), p => p.Identity == child.Identity); }
        finally { if (child != null) try { inspector.EndProcess(child, false); } catch (InvalidOperationException) { } }
    }
    [TestMethod, TestCategory("ProcessLaunchSecurity")] public void ManagedLaunchEnforcesActualIntegrityBeforePathValidation()
    {
        var integrity = ProcessTestEnvironment.Integrity();
        var inspector = Inspector();
        // Intentionally unqualified: even a permitted Medium session cannot launch it.
        var invalid = new Core.Domain.Models.LaunchSpec("unqualified-fixture-executable", [], AppContext.BaseDirectory);
        if (integrity == "Medium") Assert.Throws<ArgumentException>(() => inspector.LaunchManaged(invalid));
        else {
            var error = Microsoft.VisualStudio.TestTools.UnitTesting.Assert.ThrowsExactly<InvalidOperationException>(() => inspector.LaunchManaged(invalid));
            Assert.Equal(ProcessTestEnvironment.LaunchDeniedMessage, error.Message);
        }
    }
    [TestMethod] public async Task ResourceTablesHaveConsistentUnitsAndSeparateMemory()
    {
        var inspector = Inspector(); foreach (var resource in new[] { "CPU", "Memory", "GPU", "Disk 0" }) { var table = await inspector.ResourceAsync(resource, default); Assert.NotEmpty(table.Rows); Assert.All(table.Rows, r => Assert.Equal(3, r.Length)); if (resource == "Memory") Assert.Contains(table.Rows, r => r[0] == "Committed" && r[2] == "B"); }
    }
    [TestMethod] public async Task ControlsOperateOnOwnedFixtureProcess()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(WindowsTests).Assembly.Location); start.ArgumentList.Add("--process-fixture");
        using var child = Process.Start(start)!;
        try
        {
            var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("CORE control fixture ready", ready);
            var i = Inspector(); var sample = i.Processes().Single(p => p.Id == child.Id);
            i.SetPriority(sample, "BelowNormal"); child.Refresh(); Assert.Equal(ProcessPriorityClass.BelowNormal, child.PriorityClass);
            if (Environment.ProcessorCount <= 64) { i.SetAffinity(sample, 1); child.Refresh(); Assert.Equal((nint)1, child.ProcessorAffinity); }
            i.EndProcess(sample, false); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, child.ExitCode);
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
    }
}
[TestClass] public sealed class FileTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CORE-tests-" + Guid.NewGuid());
    private readonly FileInspector inspector = new(NullLogger<FileInspector>.Instance);
    public FileTests() { Directory.CreateDirectory(root); }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [TestMethod] public async Task ScanAggregatesRecursiveDirectories()
    {
        Directory.CreateDirectory(Path.Combine(root, "child", "nested")); await File.WriteAllBytesAsync(Path.Combine(root, "root.bin"), new byte[10]); await File.WriteAllBytesAsync(Path.Combine(root, "child", "a.bin"), new byte[20]); await File.WriteAllBytesAsync(Path.Combine(root, "child", "nested", "b.txt"), new byte[30]);
        var r = await inspector.ScanAsync(root, true, null, default); Assert.Equal(60, r.Bytes); Assert.Equal(3, r.Files); Assert.Equal(2, r.Folders); Assert.Equal(30, r.Extensions[".bin"]); Assert.Contains(r.LargestDirectories, d => d.Path == Path.Combine(root, "child") && d.Bytes == 50); Assert.Contains(r.LargestDirectories, d => d.Path == root && d.Bytes == 60);
    }
    [TestMethod] public async Task DuplicatesCompareContentNotNamesOrSize()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "equal"); await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "equal"); await File.WriteAllTextAsync(Path.Combine(root, "c.txt"), "other"); var r = await inspector.DuplicatesAsync(root, null, default); Assert.Single(r.Groups); Assert.Equal(2, r.Groups[0].Paths.Count); Assert.Equal(5, r.ReclaimableBytes);
    }
    [TestMethod] public async Task CancelledScanAndDuplicatesStop()
    {
        using var c = new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.ScanAsync(root, true, null, c.Token)); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.DuplicatesAsync(root, null, c.Token));
    }
    [TestMethod] public void CopyMoveOperateOnlyOnFixtureFiles()
    {
        var a = Path.Combine(root, "a.txt"); var b = Path.Combine(root, "b.txt"); var c = Path.Combine(root, "c.txt"); File.WriteAllText(a, "fixture"); inspector.Copy(a, b); Assert.Equal("fixture", File.ReadAllText(b)); inspector.Move(b, c); Assert.False(File.Exists(b)); Assert.True(File.Exists(c)); Assert.Throws<IOException>(() => inspector.Copy(a, c));
    }
    [TestMethod] public void SystemPathsAreDeniedBeforeMutation()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows); Assert.Throws<UnauthorizedAccessException>(() => inspector.Copy(Path.Combine(windows, "explorer.exe"), Path.Combine(root, "copy.exe")));
    }
    [TestMethod] public async Task ReparseDirectoriesAreSkipped()
    {
        File.WriteAllText(Path.Combine(root, "a.txt"), "fixture");
        var info = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/J"); info.ArgumentList.Add(Path.Combine(root, "loop")); info.ArgumentList.Add(root); using var p = Process.Start(info)!; await p.WaitForExitAsync(); Assert.Equal(0, p.ExitCode);
        var r = await inspector.ScanAsync(root, true, null, default); Assert.Equal(1, r.Files); Assert.Equal(1, r.ReparsePointsSkipped); Directory.Delete(Path.Combine(root, "loop"));
    }
    [TestMethod] public async Task HardLinksDoNotInflateDuplicateSavings()
    {
        var a = Path.Combine(root, "a.txt"); File.WriteAllText(a, "fixture"); File.Copy(a, Path.Combine(root, "b.txt"));
        var info = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/H"); info.ArgumentList.Add(Path.Combine(root, "hard.txt")); info.ArgumentList.Add(a); using var p = Process.Start(info)!; await p.WaitForExitAsync(); Assert.Equal(0, p.ExitCode);
        var r = await inspector.DuplicatesAsync(root, null, default); Assert.Equal(1, r.HardLinksSkipped); Assert.Equal(7, r.ReclaimableBytes); Assert.Equal(2, Assert.Single(r.Groups).Paths.Count);
    }
}

