using System.Runtime.InteropServices;
using Core.Application.Interfaces;
using Core.Domain.Models;
namespace Core.Application.Services;

public sealed record RuntimePlatformSnapshot(bool Windows, bool Linux, string Description, string Version,
    Architecture OSArchitecture, Architecture ProcessArchitecture, string Hostname);
public interface IRuntimePlatform { RuntimePlatformSnapshot Read(); }
public sealed class SystemRuntimePlatform : IRuntimePlatform
{
    public RuntimePlatformSnapshot Read() => new(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(),
        RuntimeInformation.OSDescription, Environment.OSVersion.VersionString, RuntimeInformation.OSArchitecture,
        RuntimeInformation.ProcessArchitecture, Environment.MachineName);
}
public sealed class PlatformInformation : IPlatformInformation
{
    public PlatformIdentity Platform { get; }
    public PlatformInformation(IRuntimePlatform? runtime = null)
    {
        var host = (runtime ?? new SystemRuntimePlatform()).Read();
        if (host.Windows && host.Linux) throw new ArgumentException("A host cannot be both Windows and Linux.");
        Platform = new(host.Windows ? PlatformKind.Windows : host.Linux ? PlatformKind.Linux : PlatformKind.Unsupported,
            host.Description, host.OSArchitecture, host.ProcessArchitecture) { Version = host.Version, Hostname = host.Hostname };
    }
}
