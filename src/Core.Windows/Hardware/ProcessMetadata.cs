using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Core.Windows.Native;
using Microsoft.Win32.SafeHandles;
namespace Core.Windows.Hardware;
internal sealed record ProcessMetadata(DateTime Started, string? Path, string? User, string? Architecture, string? Integrity)
{
    internal static ProcessMetadata? Read(int id)
    {
        using var handle = NativeMethods.OpenProcess(0x1000, false, (uint)id);
        if (handle.IsInvalid || !NativeMethods.GetProcessTimes(handle, out var start, out _, out _, out _)) return null;
        var text = new StringBuilder(32768); uint length = (uint)text.Capacity;
        var path = NativeMethods.QueryFullProcessImageNameW(handle, 0, text, ref length) ? text.ToString() : null;
        string? architecture = null, user = null, integrity = null;
        if (IsWow64Process2(handle, out var machine, out var native)) architecture = (machine == 0 ? native : machine) switch { 0x8664 => "x64", 0x14c => "x86", 0xaa64 => "ARM64", _ => "Unknown" };
        if (OpenProcessToken(handle, 8, out var token))
        {
            using (token)
            {
                ReadToken(token, 1, buffer => { var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)); try { user = sid.Translate(typeof(NTAccount)).Value; } catch (IdentityNotMappedException) { user = sid.Value; } });
                ReadToken(token, 25, buffer => { var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)); var rid = uint.Parse(sid.Value.Split('-')[^1]); integrity = rid switch { < 4096 => "Untrusted", < 8192 => "Low", < 12288 => "Medium", < 16384 => "High", _ => "System" }; });
            }
        }
        return new(DateTime.FromFileTimeUtc(start).ToLocalTime(), path, user, architecture, integrity);
    }
    private static void ReadToken(SafeAccessTokenHandle token, int kind, Action<IntPtr> read)
    {
        GetTokenInformation(token, kind, IntPtr.Zero, 0, out var size); if (size == 0 || size > 65536) return;
        var buffer = Marshal.AllocHGlobal((int)size); try { if (GetTokenInformation(token, kind, buffer, size, out _)) read(buffer); } finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort machine, out ushort native);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, IntPtr buffer, uint length, out uint needed);
}
