using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Core.Domain.Models;
using Core.Windows.Native;
using Core.Windows.Hardware;
namespace Core.Windows.Network;
public static class EndpointReader
{
    public static int Port(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16BigEndian(bytes);
    public static ConnectionRow Parse(ReadOnlySpan<byte> row, bool tcp, bool ipv6, Func<int, string> name)
    {
        string local, remote = "—", state = "Endpoint"; int port, pid; int? remotePort = null;
        if (ipv6)
        {
            local = new IPAddress(row[..16], BinaryPrimitives.ReadUInt32LittleEndian(row[16..])).ToString(); port = Port(row[20..]);
            if (tcp) { remote = new IPAddress(row.Slice(24, 16), BinaryPrimitives.ReadUInt32LittleEndian(row[40..])).ToString(); remotePort = Port(row[44..]); state = State(BinaryPrimitives.ReadUInt32LittleEndian(row[48..])); pid = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[52..]); }
            else pid = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[24..]);
        }
        else
        {
            var offset = tcp ? 4 : 0; local = new IPAddress(row.Slice(offset, 4)).ToString(); port = Port(row[(offset + 4)..]);
            if (tcp) { state = State(BinaryPrimitives.ReadUInt32LittleEndian(row)); remote = new IPAddress(row.Slice(12, 4)).ToString(); remotePort = Port(row[16..]); pid = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[20..]); }
            else pid = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
        }
        return new((tcp ? "TCP" : "UDP") + (ipv6 ? "6" : "4"), local, port, remote, remotePort, state, pid, name(pid));
    }
    private static string State(uint state) => state switch { 1 => "Closed", 2 => "Listen", 3 => "SynSent", 4 => "SynReceived", 5 => "Established", 6 => "FinWait1", 7 => "FinWait2", 8 => "CloseWait", 9 => "Closing", 10 => "LastAck", 11 => "TimeWait", 12 => "DeleteTCB", _ => $"Unknown ({state})" };
    public static IReadOnlyList<ConnectionRow> Read()
    {
        var captured = DateTimeOffset.UtcNow; var rows = new List<ConnectionRow>(); var names = new Dictionary<int, string>();
        string Name(int id) { if (names.TryGetValue(id, out var n)) return n; try { using var p = Process.GetProcessById(id); n = p.ProcessName; } catch (ArgumentException) { n = "Exited"; } catch (System.ComponentModel.Win32Exception) { n = "Access denied"; } names[id] = n; return n; }
        foreach (var tcp in new[] { true, false }) foreach (var ipv6 in new[] { false, true })
        {
            uint size = 0; var module = tcp; var family = ipv6 ? 23u : 2u;
            uint Call(IntPtr buffer) => tcp ? NativeMethods.GetExtendedTcpTable(buffer, ref size, true, family, module ? 8u : 5u, 0) : NativeMethods.GetExtendedUdpTable(buffer, ref size, true, family, 1, 0);
            var error = Call(IntPtr.Zero); if (module && error is not 122 and not 0) { module = false; size = 0; error = Call(IntPtr.Zero); } if (error != 122 && error != 0) throw new System.ComponentModel.Win32Exception((int)error);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (size < 4 || size > 128 * 1024 * 1024) throw new InvalidOperationException("Endpoint table has an invalid allocation size.");
                var allocated = size; var buffer = Marshal.AllocHGlobal(checked((int)allocated));
                try
                {
                    error = Call(buffer); if (error == 122) continue; if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
                    var count = Marshal.ReadInt32(buffer); var stride = module ? (ipv6 ? 192 : 160) : ipv6 ? (tcp ? 56 : 28) : (tcp ? 24 : 12); var offset = module ? 8 : 4;
                    if (count < 0 || offset + (long)count * stride > allocated) throw new InvalidOperationException("Invalid endpoint table size.");
                    var data = new byte[stride]; for (var i = 0; i < count; i++) { Marshal.Copy(buffer + offset + i * stride, data, 0, stride); var parsed = Parse(data, tcp, ipv6, Name); if (module) { var created = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(ipv6 ? 56 : 24)); if (created > 0) parsed = parsed with { NativeCreated = created }; } rows.Add(parsed); }
                    break;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            if (error == 122) throw new InvalidOperationException("Endpoint table changed repeatedly; retry refresh.");
        }
        // Reject owners that started after enumeration began: the PID may already have been recycled.
        var owners = new Dictionary<int, ProcessMetadata?>();
        return rows.Select(row => { if (!owners.TryGetValue(row.ProcessId, out var owner)) { owner = ProcessMetadata.Read(row.ProcessId); owners[row.ProcessId] = owner; } if (owner != null && owner.Started.ToUniversalTime() > captured.UtcDateTime) owner = null; return row with { OwnerStarted = owner?.Started, Executable = owner?.Path, User = owner?.User }; }).ToArray();
    }
}
