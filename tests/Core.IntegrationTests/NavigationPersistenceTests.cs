using Core.Application.Services;
using Core.Domain.Models;
using Core.Persistence.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class NavigationPersistenceTests
{
    [TestMethod] public void RouteExpansionAndSidebarSurviveSettingsRoundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "CORE-navigation-" + Guid.NewGuid());
        try
        {
            var store = new PreferencesStore(root); var providers = new[] { new Capability("files.inspect", true, "Fixture", "") };
            var state = new NavigationCoordinator(PlatformKind.Windows, providers); state.Select(NavigationDestination.Files); state.SetExpanded(NavigationCategory.Network, true); state.SetExpanded(NavigationCategory.Hardware, false); state.PaneOpen = false;
            store.Save(new Preferences("Dark", 5) with { Navigation = state.Snapshot() });
            var loaded = new PreferencesStore(root).Load(); var restored = new NavigationCoordinator(PlatformKind.Windows, providers, loaded.Navigation);
            Assert.Equal(NavigationDestination.Files, restored.Selected); Assert.False(restored.PaneOpen); Assert.True(restored.IsExpanded(NavigationCategory.Hardware)); Assert.True(restored.IsExpanded(NavigationCategory.Network)); Assert.Equal(5, loaded.RefreshSeconds);
        }
        finally { Directory.Delete(root, true); }
    }
    [TestMethod] public void LegacyNullAndOversizedNavigationSettingsAreSanitized()
    {
        var root = Path.Combine(Path.GetTempPath(), "CORE-navigation-" + Guid.NewGuid());
        try
        {
            var store = new PreferencesStore(root); File.WriteAllText(Path.Combine(root, "settings.json"), "{\"Theme\":\"Light\",\"Navigation\":null}"); Assert.Equal("core.overview", store.Load().Navigation.LastDestination);
            store.Save(new Preferences() with { Navigation = new(new string('x', 200), ["hardware", "hardware", "", new string('x', 100), "unknown"]) });
            var restored = new NavigationCoordinator(PlatformKind.Windows, [], store.Load().Navigation); Assert.Equal(NavigationDestination.CoreOverview, restored.Selected); Assert.True(restored.IsExpanded(NavigationCategory.Hardware));
            Assert.Equal<string>(["hardware", "unknown"], store.Load().Navigation.ExpandedCategories!);
        }
        finally { Directory.Delete(root, true); }
    }
}
