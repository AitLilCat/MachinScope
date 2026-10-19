using Core.Persistence.Settings;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class Phase5PersistenceTests
{
    [TestMethod] public void ConcurrentSettingsReadsAndWritesLeaveCompleteJsonAndNoTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "core-settings-" + Guid.NewGuid());
        try { var store = new PreferencesStore(root); Parallel.For(0, 100, i => { store.Save(new Preferences(i % 2 == 0 ? "Dark" : "Light", 2)); Assert.Equal(2, store.Load().RefreshSeconds); }); Assert.True(store.Load().Theme is "Dark" or "Light"); Assert.Equal(0, Directory.GetFiles(root, "*.tmp").Length); }
        finally { Directory.Delete(root, true); }
    }
    [TestMethod] public void CorruptSettingsRecoverPreviousValidatedVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "core-settings-" + Guid.NewGuid());
        try { var store = new PreferencesStore(root); store.Save(new("Dark", 5)); store.Save(new("Light", 1)); File.WriteAllText(Path.Combine(root, "settings.json"), "{invalid"); var recovered = store.Load(); Assert.Equal("Dark", recovered.Theme); Assert.Equal(5, recovered.RefreshSeconds); Assert.True(File.Exists(Path.Combine(root, "settings.json.corrupt"))); Assert.Contains("Dark", File.ReadAllText(Path.Combine(root, "settings.json"))); File.WriteAllText(Path.Combine(root, "settings.json.bak"), "invalid"); File.WriteAllText(Path.Combine(root, "settings.json"), "invalid"); Assert.Equal("System", store.Load().Theme); }
        finally { Directory.Delete(root, true); }
    }
}
