using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Core.Domain.Models;
using Microsoft.Win32.SafeHandles;
namespace Core.Windows.Devices;

internal static class DeviceNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Info { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    internal sealed class InfoSet : SafeHandleZeroOrMinusOneIsInvalid
    {
        public InfoSet() : base(true) { }
        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern InfoSet SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(InfoSet set, uint index, ref Info info);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceIdW(InfoSet set, ref Info info, StringBuilder buffer, uint count, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryPropertyW(InfoSet set, ref Info info, uint property, out uint type, byte[]? buffer, uint length, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDevicePropertyW(InfoSet set, ref Info info, ref PropertyKey key, out uint type, byte[]? buffer, uint length, out uint required, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiOpenDeviceInfoW(InfoSet set, string instance, IntPtr parent, uint flags, ref Info info);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

    private static Info NewInfo() => new() { Size = (uint)Marshal.SizeOf<Info>() };
    private static InfoSet Open()
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 2 | 4); // PRESENT | ALLCLASSES
        if (!set.IsInvalid) return set;
        var error = Marshal.GetLastWin32Error(); set.Dispose(); throw new Win32Exception(error);
    }
    private static byte[]? RegistryProperty(InfoSet set, ref Info info, uint property)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, null, 0, out var size);
        if (size == 0 || size > 65536) return null;
        var buffer = new byte[size];
        return SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, size, out _) ? buffer : null;
    }
    private static string Text(byte[]? value) => value == null ? "" : Encoding.Unicode.GetString(value).TrimEnd('\0');
    private static string[] Multi(byte[]? value) => Text(value).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    private static byte[]? Property(InfoSet set, ref Info info, Guid format, uint id, uint expectedType)
    {
        var key = new PropertyKey { Format = format, Id = id };
        SetupDiGetDevicePropertyW(set, ref info, ref key, out _, null, 0, out var size, 0);
        if (size == 0 || size > 65536) return null;
        var buffer = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref info, ref key, out var type, buffer, size, out _, 0) && type == expectedType ? buffer : null;
    }
    private static DeviceRecord Read(InfoSet set, ref Info info)
    {
        var id = new StringBuilder(512);
        if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var name = Text(RegistryProperty(set, ref info, 12));
        if (name.Length == 0) name = Text(RegistryProperty(set, ref info, 0));
        if (name.Length == 0) name = id.ToString();
        var parent = new StringBuilder(512);
        if (CM_Get_Parent(out var parentNode, info.DevInst, 0) != 0 || CM_Get_Device_IDW(parentNode, parent, (uint)parent.Capacity, 0) != 0) parent.Clear();
        var statusAvailable = CM_Get_DevNode_Status(out var status, out var problem, info.DevInst, 0) == 0;
        var container = Property(set, ref info, new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2, 13);
        var capabilities = RegistryProperty(set, ref info, 15);
        return new(id.ToString(), name, Text(RegistryProperty(set, ref info, 11)), Text(RegistryProperty(set, ref info, 7)), info.ClassGuid.ToString("B"),
            Text(RegistryProperty(set, ref info, 22)), Text(RegistryProperty(set, ref info, 4)), Text(RegistryProperty(set, ref info, 9)), parent.ToString(),
            Text(RegistryProperty(set, ref info, 13)), Multi(RegistryProperty(set, ref info, 1)), Multi(RegistryProperty(set, ref info, 2)),
            container is { Length: 16 } ? new Guid(container).ToString() : "", statusAvailable ? status : null, statusAvailable ? problem : null,
            capabilities is { Length: >= 4 } ? (BitConverter.ToUInt32(capabilities) & 4) != 0 : null);
    }
    internal static IReadOnlyList<DeviceRecord> Enumerate(CancellationToken token)
    {
        using var set = Open(); var result = new List<DeviceRecord>();
        for (uint index = 0; ; index++)
        {
            token.ThrowIfCancellationRequested(); var info = NewInfo();
            if (!SetupDiEnumDeviceInfo(set, index, ref info))
            {
                var error = Marshal.GetLastWin32Error(); if (error == 259) break; // NO_MORE_ITEMS
                throw new Win32Exception(error);
            }
            try { result.Add(Read(set, ref info)); }
            catch (Win32Exception e) when (e.NativeErrorCode is 433 or 1167) { } // Removed during enumeration.
        }
        token.ThrowIfCancellationRequested(); return result;
    }
    internal static IReadOnlyDictionary<string, string> DetailProperties(string instanceId)
    {
        using var set = Open(); var info = NewInfo();
        if (!SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var driverFormat = new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
        var date = Property(set, ref info, driverFormat, 2, 16);
        return new Dictionary<string, string> {
            ["Driver version"] = Text(Property(set, ref info, driverFormat, 3, 18)),
            ["Driver provider"] = Text(Property(set, ref info, driverFormat, 9, 18)),
            ["Driver INF"] = Text(Property(set, ref info, driverFormat, 5, 18)),
            ["Driver date"] = date is { Length: 8 } ? DateTime.FromFileTimeUtc(BitConverter.ToInt64(date)).ToString("O") : "",
            ["Bus-reported description"] = Text(Property(set, ref info, new("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4, 18)),
            ["Location paths"] = string.Join("\n", Multi(RegistryProperty(set, ref info, 35)))
        };
    }
}
