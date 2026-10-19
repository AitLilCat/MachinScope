using System.Runtime.InteropServices;
using Core.Domain.Models;
namespace Core.Windows.FileSystem;
public static class FileLocks
{
    [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess { public uint Id; public System.Runtime.InteropServices.ComTypes.FILETIME Start; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessInfo { public UniqueProcess Process; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service; public uint Type, Status, Session; [MarshalAs(UnmanagedType.Bool)] public bool Restartable; }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint handle, uint flags, System.Text.StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint handle, uint files, string[] names, uint applications, IntPtr processes, uint services, IntPtr serviceNames);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] ProcessInfo[]? list, ref uint reason);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint handle);
    public static InspectionTable Inspect(string path)
    {
        path = Path.GetFullPath(path); if (!File.Exists(path)) throw new FileNotFoundException("Choose an existing file.", path);
        var error = RmStartSession(out var handle, 0, new System.Text.StringBuilder(33)); if (error != 0) throw new System.ComponentModel.Win32Exception(error);
        try
        {
            error = RmRegisterResources(handle, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero); if (error != 0) throw new System.ComponentModel.Win32Exception(error);
            uint count = 0, reason = 0; error = RmGetList(handle, out var needed, ref count, null, ref reason);
            if (error == 0) return new("Restart Manager file users", ["PID", "Application", "Service", "Restartable"], [], "No registered users reported. This does not prove the file is unlocked.");
            for (var attempt = 0; attempt < 3 && error == 234; attempt++)
            {
                if (needed > 100000) throw new InvalidOperationException("Restart Manager list exceeds limit."); var list = new ProcessInfo[needed]; count = needed; error = RmGetList(handle, out needed, ref count, list, ref reason);
                if (error == 0) return new("Restart Manager file users", ["PID", "Application", "Service", "Restartable"], list.Take((int)count).Select(p => new[] { p.Process.Id.ToString(), p.Name, p.Service, p.Restartable.ToString() }).ToArray(), "Read-only Restart Manager query. MachinScope never closes applications to unlock files.");
            }
            throw new System.ComponentModel.Win32Exception(error);
        }
        finally { RmEndSession(handle); }
    }
}
