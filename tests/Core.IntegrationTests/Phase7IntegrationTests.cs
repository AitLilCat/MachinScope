using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Windows;
using Core.Windows.Devices;
using Core.Windows.FileSystem;
using Core.Windows.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class Phase7IntegrationTests
{
    private static ServiceProvider Providers()
    {
        var services = new ServiceCollection(); services.AddSingleton<ILogger<SystemInspector>>(NullLogger<SystemInspector>.Instance);
        services.AddSingleton<ILogger<FileInspector>>(NullLogger<FileInspector>.Instance); services.AddCoreWindows(); return services.BuildServiceProvider();
    }
    [TestMethod] public void WindowsServiceAliasesShareOneInstanceAndDisposeSafely()
    {
        var providers = Providers(); var inspector = providers.GetRequiredService<SystemInspector>();
        foreach (var type in new[] { typeof(ISystemInspector), typeof(IProcessInspector), typeof(IProcessController), typeof(IPerformanceCollector), typeof(INetworkInspector), typeof(IStorageInspector), typeof(IStartupManager), typeof(IServiceInspector), typeof(IConnectionTimeline) }) Assert.True(ReferenceEquals(inspector, providers.GetRequiredService(type)));
        var device = providers.GetRequiredService<WindowsDeviceInspector>(); Assert.True(ReferenceEquals(device, providers.GetRequiredService<IDeviceInspector>())); Assert.True(ReferenceEquals(device, providers.GetRequiredService<IUsbInspector>()));
        providers.Dispose(); providers.Dispose(); device.Dispose();
    }
    [TestMethod] public async Task CanonicalStorageSnapshotReusesInventoryAndRetainsNativeSubjects()
    {
        using var providers = Providers(); var storage = providers.GetRequiredService<IStorageSnapshots>();
        var first = await storage.ReadAsync(false, default); var second = await storage.ReadAsync(false, default);
        Assert.True(ReferenceEquals(first, second)); Assert.True(ReferenceEquals(first, storage.Last));
        Assert.NotEmpty(first.Inventory.Disks); Assert.Contains(first.Table.Data!, r => r.Subject is Core.Domain.Models.DiskTarget);
        Assert.Contains(first.Table.Data!, r => r.Cells[0] == "Partition"); Assert.Contains(first.Table.Data!, r => r.Cells[0] == "Volume");
        Assert.True(((StorageSnapshots)storage).LastSharedQueryHits >= 4);
    }
    [TestMethod] public async Task ServicesInspectorReadsRealStableServiceIdentitiesWithoutMutating()
    {
        using var providers = Providers(); var services = await providers.GetRequiredService<IServiceInspector>().ServicesAsync(default);
        Assert.NotEmpty(services.Rows); Assert.Equal(services.Rows.Count, services.Data!.Select(r => r.Key).Distinct().Count());
        Assert.Contains("Read-only", services.Notice!); Assert.True(services.Data!.All(r => r.Subject == null));
    }
    [TestMethod] public void ThemeResourceKeysAndLightContrastRemainConsistent()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root != null && !File.Exists(Path.Combine(root.FullName, "CORE.slnx"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Source checkout is required for the theme contract check.");
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName, "src/Core.Desktop/App.xaml"));
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dictionaries = document.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(x + "Key") != null).ToDictionary(e => (string)e.Attribute(x + "Key")!);
        foreach (var theme in new[] { "Light", "Default", "HighContrast" }) Assert.True(dictionaries.ContainsKey(theme));
        var keys = dictionaries["Light"].Elements().Select(e => (string)e.Attribute(x + "Key")!).Order().ToArray();
        foreach (var theme in new[] { "Default", "HighContrast" }) Assert.Equal<string>(keys, dictionaries[theme].Elements().Select(e => (string)e.Attribute(x + "Key")!).Order());
        var lightSurface = dictionaries["Light"].Elements().Single(e => (string?)e.Attribute(x + "Key") == "CoreSurfaceBackground"); Assert.Equal("#FFFFFF", (string)lightSurface.Attribute("Color")!);
    }
}
