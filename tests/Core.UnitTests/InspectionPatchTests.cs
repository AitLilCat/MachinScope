using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class InspectionPatchTests
{
    [TestMethod] public void ExplicitColumnWidthsSurviveSpareSpaceAndViewportChanges()
    {
        ColumnLayout[] layout = [new("Disk", 205, true, 0), new("Model", 350, true, 1), new("Hidden", 100, false, 2)];
        var displayed = TableColumns.Display(layout, 1200, false);
        Assert.Equal(834d, displayed[0].Width);
        var resized = displayed.Select(c => c.Name == "Disk" ? c with { Width = 140 } : c).ToArray();
        foreach (var viewport in new[] { 600d, 1200, 2400 }) {
            var refreshed = TableColumns.Display(resized, viewport, true);
            Assert.Equal(140d, refreshed[0].Width); Assert.Equal(350d, refreshed[1].Width);
            Assert.Equal(100d, refreshed[2].Width); Assert.False(refreshed[2].Visible);
        }
    }
    [TestMethod] public void UnverifiedProcessInspectionKeepsFrozenIdentityAndDoesNotInventLiveTabs()
    {
        var sample = new ProcessSample(123, 1, "Selected process", null, "selected.exe", null, 42, null, null, null, "Unavailable", null, null, "Access denied");
        var details = ProcessInspection.Snapshot(sample, "Access denied; selected snapshot only");
        Assert.Contains(details.General.Rows, r => r[0] == "Executable" && r[1] == "selected.exe");
        Assert.Contains(details.General.Rows, r => r[0] == "PID" && r[1] == "123");
        Assert.Contains(details.General.Rows, r => r[0] == "Inspection status" && r[1].Contains("snapshot only"));
        Assert.Equal(1, details.Modules.Rows.Count); Assert.Equal("Status", details.Modules.Columns[0]);
    }
    [TestMethod] public void AdapterConnectionsMatchConcreteAddressesWithoutAttributingWildcardListeners()
    {
        var values = new[] { "adapter", "description", "Up", "", "", "192.0.2.1/24; 2001:db8::1/64" };
        var adapter = new InspectionRow("guid", values, values, new Dictionary<string, object?>());
        ConnectionRow Endpoint(string address) => new("TCP6", address, 1234, "2001:db8::2", 443, "Established", 2, "owner");
        var result = NetworkInspection.ForAdapter(adapter, [Endpoint("192.0.2.1"), Endpoint("2001:0db8::1"), Endpoint("0.0.0.0"), Endpoint("::"), Endpoint("192.0.2.9")]);
        Assert.Equal(2, result.Count);
        var udp = NetworkInspection.Properties(Endpoint("192.0.2.1") with { Protocol = "UDP4", RemotePort = null });
        Assert.Contains(udp.Rows, r => r[0] == "Remote address" && r[1].Contains("no remote peer"));
        Assert.Contains(udp.Rows, r => r[0] == "Owner started" && r[1].Contains("cannot be verified"));
    }
    [TestMethod] public void TrafficRetainsOriginalAdapterSnapshotForInspection()
    {
        var raw = Enumerable.Range(0, 19).Select(i => i.ToString()).ToArray();
        var adapter = new InspectionRow("stable-guid", raw, raw, new Dictionary<string, object?>(), "stable-guid");
        var traffic = NetworkTables.Traffic(new("Adapters", raw, [raw], Data: [adapter]));
        Assert.True(ReferenceEquals(adapter, traffic.Data![0].Subject));
        Assert.Equal(adapter.Key, traffic.Data[0].Key);
    }
    [TestMethod] public void TrafficDistinguishesUnavailableProvidersDownInterfacesAndSampling()
    {
        string Status(string state, string total, string rate) {
            var values = Enumerable.Repeat("Unavailable", 19).ToArray(); values[2] = state; values[11] = total; values[15] = rate;
            return NetworkTables.Traffic(new("Adapters", values, [values])).Rows[0][10];
        }
        Assert.Contains("provider unavailable", Status("Unavailable", "Unavailable", "Unavailable"));
        Assert.Contains("Interface Down", Status("Down", "0", "Unavailable"));
        Assert.Contains("second sample", Status("Up", "42", "Unavailable"));
        Assert.Equal("Available", Status("Up", "42", "0"));
    }
}
