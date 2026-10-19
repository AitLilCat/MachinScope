using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Core.Application.Interfaces;
namespace Core.Windows.Network;
public sealed class NetworkDiagnostics : INetworkDiagnostics
{
    public async Task<string> RunAsync(string kind, string host, int port, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace)) throw new ArgumentException("Enter a hostname or IP address.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(kind == "Traceroute" ? 60 : 10)); var ct = timeout.Token;
        switch (kind)
        {
            case "DNS": return string.Join(Environment.NewLine, (await Dns.GetHostAddressesAsync(host, ct)).Select(a => a.ToString()));
            case "Reverse DNS": return (await Dns.GetHostEntryAsync(host, ct)).HostName;
            case "TCP":
                if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
                using (var client = new TcpClient()) { await client.ConnectAsync(host, port, ct); return $"Connected to {host}:{port}"; }
            case "Ping":
                using (var ping = new Ping()) { var r = await ping.SendPingAsync(host, TimeSpan.FromSeconds(3), new byte[32], null, ct); return $"{r.Status} · {r.Address} · {r.RoundtripTime} ms"; }
            case "Traceroute":
                var addresses = await Dns.GetHostAddressesAsync(host, ct); var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? throw new NotSupportedException("IPv4 traceroute only."); var output = new StringBuilder();
                for (var ttl = 1; ttl <= 20; ttl++) { ct.ThrowIfCancellationRequested(); using var ping = new Ping(); var r = await ping.SendPingAsync(address, TimeSpan.FromSeconds(2), new byte[32], new PingOptions(ttl, true), ct); output.AppendLine($"{ttl,2}  {r.Address?.ToString() ?? "*"}  {r.Status}  {r.RoundtripTime} ms"); if (r.Status == IPStatus.Success) break; }
                return output.ToString();
            default: throw new ArgumentException("Unknown diagnostic.");
        }
    }
}
