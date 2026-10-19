using Core.Windows.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;
[TestClass] public sealed class StartupTests
{
    private readonly string path = @"Software\CORE-Tests\" + Guid.NewGuid();
    private readonly string backup = Path.Combine(Path.GetTempPath(), "CORE-startup-tests-" + Guid.NewGuid());
    private RegistryKey Root() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
    [TestCleanup] public void Cleanup() { using var root = Root(); root.DeleteSubKeyTree(path, false); if (Directory.Exists(backup)) Directory.Delete(backup, true); }
    [TestMethod] public void DisableAndRestorePreserveExactCommandAndRegistryKind()
    {
        using var root = Root(); using var key = root.CreateSubKey(path); const string command = "\"%TEMP%\\fixture.exe\" --quoted \"λ value\""; key.SetValue("Fixture", command, RegistryValueKind.ExpandString);
        var controller = new StartupController(NullLogger.Instance, path, backup); var entry = Assert.Single(controller.Read()); controller.SetEnabled(entry, false); Assert.Null(key.GetValue("Fixture")); var disabled = Assert.Single(new StartupController(NullLogger.Instance, path, backup).Read()); Assert.True(disabled.Disabled); Assert.Equal(command, disabled.Command); controller.SetEnabled(disabled, true); Assert.Equal(command, key.GetValue("Fixture", null, RegistryValueOptions.DoNotExpandEnvironmentNames)); Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Fixture")); Assert.Equal(0, Directory.EnumerateFiles(backup, "*.json").Count());
    }
    [TestMethod] public void ChangedEntriesAndRestoreConflictsAreRefusedWithBackupRetained()
    {
        using var root = Root(); using var key = root.CreateSubKey(path); key.SetValue("Fixture", "original"); var controller = new StartupController(NullLogger.Instance, path, backup); var entry = Assert.Single(controller.Read()); key.SetValue("Fixture", "changed"); Assert.Throws<InvalidOperationException>(() => controller.SetEnabled(entry, false)); Assert.Equal("changed", key.GetValue("Fixture")); key.SetValue("Fixture", "original"); controller.SetEnabled(entry, false); var disabled = Assert.Single(controller.Read()); key.SetValue("Fixture", "replacement"); Assert.Throws<InvalidOperationException>(() => controller.SetEnabled(disabled, true)); Assert.Equal("replacement", key.GetValue("Fixture")); Assert.Equal(1, Directory.EnumerateFiles(backup, "*.json").Count());
    }
    [TestMethod] public void UnsupportedSourceCannotBeMutated()
    {
        using var root = Root(); using var key = root.CreateSubKey(path); key.SetValue("Fixture", "original"); var controller = new StartupController(NullLogger.Instance, path, backup); var entry = Assert.Single(controller.Read()); Assert.Throws<InvalidOperationException>(() => controller.SetEnabled(entry with { RegistryPath = StartupController.UserRun }, false)); Assert.Equal("original", key.GetValue("Fixture"));
    }
}
