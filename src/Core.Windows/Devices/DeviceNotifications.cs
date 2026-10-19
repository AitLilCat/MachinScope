using System.Runtime.InteropServices;
namespace Core.Windows.Devices;

internal sealed class DeviceNotifications : IDisposable
{
    [StructLayout(LayoutKind.Explicit, Size = 416)] private struct Filter
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Flags;
        [FieldOffset(8)] public uint Type;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint Callback(IntPtr notification, IntPtr context, uint action, IntPtr data, uint size);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Register_Notification(ref Filter filter, IntPtr context, Callback callback, out IntPtr notification);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Unregister_Notification(IntPtr notification);
    private readonly Callback callback;
    private IntPtr interfaces, instances;
    public string Status { get; }
    public bool Complete { get; }
    private int callbackFailures;
    public int CallbackFailures => Volatile.Read(ref callbackFailures);
    public DeviceNotifications(Action<uint, string> changed)
    {
        callback = (_, _, action, data, size) => {
            // No collector calls, disk access or exceptions may escape into the PnP callback.
            var path = "";
            try {
                var type = data != IntPtr.Zero && size >= 8 ? Marshal.ReadInt32(data) : -1;
                var offset = type == 0 ? 24 : type == 2 ? 8 : (int)size;
                path = size > offset && size <= 65536 ? Marshal.PtrToStringUni(IntPtr.Add(data, offset), ((int)size - offset) / 2)?.TrimEnd('\0') ?? "" : "";
            } catch { Interlocked.Increment(ref callbackFailures); }
            try { changed(action, path); } catch { Interlocked.Increment(ref callbackFailures); }
            return 0;
        };
        var interfaceFilter = new Filter { Size = 416, Flags = 1, Type = 0 };
        var instanceFilter = new Filter { Size = 416, Flags = 2, Type = 2 };
        var a = CM_Register_Notification(ref interfaceFilter, IntPtr.Zero, callback, out interfaces);
        var b = CM_Register_Notification(ref instanceFilter, IntPtr.Zero, callback, out instances);
        Complete = a == 0 && b == 0;
        Status = Complete ? "Native PnP notifications active; inventory cached until change" : $"PnP notifications incomplete (CONFIGRET {a}/{b}); active-page fallback every 5 seconds";
    }
    public void Dispose()
    {
        if (interfaces != IntPtr.Zero) { CM_Unregister_Notification(interfaces); interfaces = IntPtr.Zero; }
        if (instances != IntPtr.Zero) { CM_Unregister_Notification(instances); instances = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}
