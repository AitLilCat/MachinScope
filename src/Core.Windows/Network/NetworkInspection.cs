using System.Net.NetworkInformation;
using Core.Domain.Models;
namespace Core.Windows.Hardware;
public sealed partial class SystemInspector
{
    public IReadOnlyList<ConnectionEvent> ConnectionEvents() { lock (connectionLock) return connections.Events; }
    public InspectionTable DnsConfiguration()
    {
        var rows = NetworkInterface.GetAllNetworkInterfaces().SelectMany(a => a.GetIPProperties().DnsAddresses.Select(d => new[] { a.Name, d.ToString() })).ToArray();
        return new("DNS configuration", ["Interface", "DNS server"], rows, "Use Diagnose for explicit lookups. Resolver cache enumeration is unavailable through this provider.");
    }
}
