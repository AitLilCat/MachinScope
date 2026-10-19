using System.Runtime.InteropServices;
using Core.Application.Services;
using Core.Domain.Models;
using Core.Windows.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class PlatformInformationTests
{
    private sealed class Runtime(bool windows, bool linux) : IRuntimePlatform
    { public RuntimePlatformSnapshot Read() => new(windows, linux, "Fixture OS description", "kernel/build fixture", Architecture.Arm64, Architecture.X64, "fixture-host"); }
    [TestMethod] public void DetectionUsesRuntimeFactsRatherThanDescriptionOrExecutableNames()
    {
        foreach (var (windows, linux, expected) in new[] { (true, false, PlatformKind.Windows), (false, true, PlatformKind.Linux), (false, false, PlatformKind.Unsupported) }) {
            var info = new PlatformInformation(new Runtime(windows, linux)); Assert.Equal(expected, info.Platform.Kind);
            Assert.Equal("Fixture OS description", info.Platform.Description); Assert.Equal("fixture-host", info.Platform.Hostname);
            Assert.Equal(Architecture.Arm64, info.Platform.OSArchitecture); Assert.Equal("kernel/build fixture", info.Platform.Version);
        }
        Assert.Throws<ArgumentException>(() => new PlatformInformation(new Runtime(true, true)));
    }
    [TestMethod] public void WindowsProvidersRefuseLinuxAndUnknownHostsBeforeRegistration()
    {
        Assert.Throws<PlatformNotSupportedException>(() => new WindowsPlatform(new PlatformInformation(new Runtime(false, true))));
        Assert.Throws<PlatformNotSupportedException>(() => new WindowsPlatform(new PlatformInformation(new Runtime(false, false))));
    }
}
