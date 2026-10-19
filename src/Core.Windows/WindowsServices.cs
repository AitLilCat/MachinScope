using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Windows.Devices;
using Core.Windows.FileSystem;
using Core.Windows.Hardware;
using Core.Windows.Network;
using Microsoft.Extensions.DependencyInjection;
namespace Core.Windows;

public static class WindowsServices
{
    public static IServiceCollection AddCoreWindows(this IServiceCollection services, WindowsPlatform? platform = null)
    {
        platform ??= new WindowsPlatform();
        services.AddSingleton<IPlatformInformation>(platform);
        services.AddSingleton<IPlatformCapabilities>(platform); services.AddSingleton<IPrivilegeService>(platform);
        services.AddSingleton<IAdministrationTools>(_ => new WindowsAdministrationTools(platform, platform));
        services.AddSingleton<IFilePropertiesInspector, FilePropertiesInspector>(); services.AddSingleton<ActionService>();
        services.AddSingleton<IDiskInspector, DiskInspector>(); services.AddSingleton<IDesktopIntegration, WindowsDesktopIntegration>();
        services.AddSingleton<WindowsDeviceInspector>();
        services.AddSingleton<IDeviceInspector>(p => p.GetRequiredService<WindowsDeviceInspector>());
        services.AddSingleton<IUsbInspector>(p => p.GetRequiredService<WindowsDeviceInspector>());
        services.AddSingleton<SystemInspector>();
        services.AddSingleton<ISystemInspector>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IProcessInspector>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IProcessController>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IPerformanceCollector>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<INetworkInspector>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IStorageInspector>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IStartupManager>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IServiceInspector>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IConnectionTimeline>(p => p.GetRequiredService<SystemInspector>());
        services.AddSingleton<IStorageSnapshots, StorageSnapshots>();
        services.AddSingleton<IFileInspector, FileInspector>(); services.AddSingleton<INetworkDiagnostics, NetworkDiagnostics>();
        return services;
    }
}
