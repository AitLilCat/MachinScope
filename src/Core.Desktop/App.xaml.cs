using Core.Application.Services;
using Core.Application.Interfaces;
using Core.Desktop.ViewModels;
using Core.Persistence.Settings;
using Core.Windows.Hardware;
using Core.Windows;
using Core.Windows.FileSystem;
using Core.Windows.Network;
using Core.Windows.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
namespace Core.Desktop;
public partial class App : Microsoft.UI.Xaml.Application
{
    private ServiceProvider? services;
    private MainWindow? window;
    public App() { InitializeComponent(); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (await VerifyPortableBuild()) { Exit(); return; }
        var information = new PlatformInformation();
        if (information.Platform.Kind != Core.Domain.Models.PlatformKind.Windows) throw new PlatformNotSupportedException("This WinUI executable requires Windows. A Linux desktop host is not implemented.");
        var platform = new WindowsPlatform(information); // Resolve OS before constructing collectors.
        var store = new PreferencesStore(); var settings = store.Load();
        var collection = new ServiceCollection(); collection.AddSingleton<IPreferencesStore>(store);
        collection.AddLogging(b => b.AddProvider(new FileLoggerProvider(Path.Combine(store.DataDirectory, "logs"), Enum.TryParse<LogLevel>(settings.LogLevel, out var level) ? level : LogLevel.Information, settings.LogRetentionDays)));
        collection.AddCoreWindows(platform); collection.AddSingleton<ShellViewModel>();
        services = collection.BuildServiceProvider();
        UnhandledException += (_, e) => { services.GetRequiredService<ILogger<App>>().LogCritical(e.Exception, "Unhandled UI exception"); };
        window = new MainWindow(services.GetRequiredService<ShellViewModel>()); window.Closed += async (_, _) => await services.DisposeAsync(); window.Activate();
    }
}
