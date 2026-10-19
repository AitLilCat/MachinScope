using System.Security.Principal;
using Core.Application.Interfaces;
using Core.Application.Services;
using Core.Domain.Models;
using Core.Windows.FileSystem;
namespace Core.Windows.Hardware;

public sealed class WindowsPlatform : IPlatformCapabilities, IPrivilegeService
{
    public PlatformIdentity Platform { get; }
    public WindowsPlatform(IPlatformInformation? information = null) { Platform = (information ?? new PlatformInformation()).Platform; if (Platform.Kind != PlatformKind.Windows) throw new PlatformNotSupportedException("The WinUI host and Windows providers require Windows. Linux desktop support is not installed."); }
    public IReadOnlyList<Capability> Capabilities => Registry.Snapshot;
    public CapabilityRegistry Registry { get; } = new([
        new("platform.inspect", true, "Windows process token", "Read-only platform and current account inspection"),
        new("os.inspect", true, "Windows / WMI", "Read-only inventory and environment information"),
        new("windows.tools", true, "Native Windows utilities", "On-demand availability checks; Windows handles tool permissions and UAC"),
        new("services.inspect", true, "Win32_Service", "Read-only status; management actions and scheduled tasks are unsupported"),
        new("services.control", false, "None", "Service changes require a dedicated validated action provider"),
        new("images.inspect", false, "None", "ISO creation, inspection and imaging providers are not implemented"),
        new("firewall.inspect", false, "None", "Firewall state provider is not implemented; no inferred security status"),
        new("performance.inspect", true, "Windows counters", "Some counters depend on hardware and permissions"),
        new("network.inspect", true, "IP Helper / network interfaces", "Interface counters; packet capture and reliable initiator direction are unavailable"),
        new("network.diagnostics", true, "DNS / ICMP / TCP", "Requests run only when explicitly invoked"),
        new("startup.inspect", true, "Registry / startup folders", "Current-user and system startup inventory"),
        new("startup.control", true, "Current-user Run registry", "Exact entry revalidation and recovery backup; no elevated system mutation"),
        new("process.inspect", true, "Win32 / Toolhelp", "Per-target access restrictions apply"),
        new("process.control", true, "Win32", "Creation identity and protection are revalidated on a native handle"),
        new("devices.inspect", true, "SetupAPI / Configuration Manager", "Read-only inventory and properties; native notifications with explicit fallback if unavailable"),
        new("devices.control", false, "None", "Per-device disruptive actions and protected image writing are not implemented"),
        new("disk.inspect", true, "Windows WMI storage providers", "Health fields depend on hardware, provider and permissions"),
        new("disk.mutate", false, "None", "Protected native operations and virtual-disk tests are pending"),
        new("files.inspect", true, "Windows filesystem", "Reparse points are skipped during traversal"),
        new("linux.host", false, "None", "A Linux desktop host and runtime validation are pending")]);
    public PrivilegeSnapshot Current()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new(identity.Name, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Windows process token; explicit UAC required for elevated operations");
    }
}
public sealed class WindowsDesktopIntegration : IDesktopIntegration
{
    public void Open(string path) => ShellActions.Open(path);
    public void ShowInFolder(string path) => ShellActions.ShowInExplorer(path);
    public void Properties(string path) => ShellActions.Properties(path);
    public InspectionTable FileUsers(string path) => FileLocks.Inspect(path);
    public void OpenDiskUtility() { using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mmc.exe")) { UseShellExecute = true, ArgumentList = { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "diskmgmt.msc") } }); }
}
