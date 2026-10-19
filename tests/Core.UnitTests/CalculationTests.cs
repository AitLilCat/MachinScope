using Core.Domain.Models;
using Core.Windows.Network;
using Core.Persistence.Settings;
using Core.Application.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;
[TestClass] public class CalculationTests
{
    [TestMethod] public void CpuKernelIncludesIdle() => Assert.Equal(75d, Calculations.Cpu(25, 60, 40, 0, 0, 0));
    [TestMethod] public void CpuNoDeltaIsUnavailable() => Assert.Null(Calculations.Cpu(0, 0, 0, 0, 0, 0));
    [TestMethod] public void CpuResetIsUnavailable() => Assert.Null(Calculations.Cpu(1, 2, 3, 2, 3, 4));
    [TestMethod] public void CounterRatesResetAndElapsed() { Assert.Equal(50d, Calculations.Rate(200, 100, 2)); Assert.Null(Calculations.Rate(1, 2, 1)); Assert.Null(Calculations.Rate(2, 1, 0)); }
    [TestMethod] public void HistoryIsBounded() { var b = new RollingBuffer<int>(2); b.Add(1); b.Add(2); b.Add(3); Assert.Equal<int>(new[] { 2, 3 }, b.Values); }
    [TestMethod] public void ProcessIdentityIncludesStartTime() { var p = new ProcessSample(12, 0, "test", DateTime.UtcNow, null, null, 0, 0, 0, null, "Normal", null, null, null); Assert.NotEqual(p.Identity, (p with { Started = p.Started?.AddSeconds(1) }).Identity); }
    [TestMethod] public void TcpIpv4ParsesNetworkByteOrderPortAndPid()
    {
        byte[] row = new byte[24]; row[0] = 5; row[4] = 127; row[7] = 1; row[8] = 0x1f; row[9] = 0x90; row[12] = 10; row[15] = 2; row[16] = 1; row[17] = 0xbb; row[20] = 42;
        var parsed = EndpointReader.Parse(row, true, false, _ => "fixture"); Assert.Equal("127.0.0.1", parsed.LocalAddress); Assert.Equal(8080, parsed.LocalPort); Assert.Equal(443, parsed.RemotePort); Assert.Equal("Established", parsed.State); Assert.Equal(42, parsed.ProcessId);
    }
    [TestMethod] public void UdpIpv6HasNoRemoteState()
    {
        byte[] row = new byte[28]; row[15] = 1; row[20] = 0; row[21] = 53; row[24] = 8;
        var parsed = EndpointReader.Parse(row, false, true, _ => "fixture"); Assert.Equal("::1", parsed.LocalAddress); Assert.Null(parsed.RemotePort); Assert.Equal("Endpoint", parsed.State); Assert.Equal(53, parsed.LocalPort);
    }
    [TestMethod] public void SettingsPersistAndValidate()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); try { var s = new PreferencesStore(root); s.Save(new(Theme: "Dark", RefreshSeconds: 5)); Assert.Equal("Dark", s.Load().Theme); Assert.Equal(5, s.Load().RefreshSeconds); s.Save(new(RefreshSeconds: 99)); Assert.Equal(2, s.Load().RefreshSeconds); File.WriteAllText(Path.Combine(root, "settings.json"), "broken"); Assert.Equal("Dark", s.Load().Theme); Assert.Equal(5, s.Load().RefreshSeconds); Assert.True(File.Exists(Path.Combine(root, "settings.json.corrupt"))); } finally { Directory.Delete(root, true); }
    }
    [TestMethod] public async Task CsvEscapesAndNeutralizesFormulaCells()
    {
        var path = Path.GetTempFileName(); try { await ExportService.WriteAsync(path, new("fixture", ["Name"], [new[] { "=CMD(\"x\")" }]), default); Assert.Contains("\"'=CMD(\"\"x\"\")\"", await File.ReadAllTextAsync(path)); } finally { File.Delete(path); }
    }
}

