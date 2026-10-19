using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Core.Windows.FileSystem;
public static class ShellActions
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellExecuteInfo { public int Size; public uint Mask; public IntPtr Window; [MarshalAs(UnmanagedType.LPWStr)] public string Verb; [MarshalAs(UnmanagedType.LPWStr)] public string File; public IntPtr Parameters, Directory; public int Show; public IntPtr Instance, IdList, Class; public IntPtr ClassKey; public uint HotKey; public IntPtr Icon, Process; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
    public static void ShowInExplorer(string path) { var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false }; info.ArgumentList.Add("/select," + Path.GetFullPath(path)); using var p = Process.Start(info); }
    public static void Open(string path) { using var p = Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true }); }
    public static void Properties(string path) { var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 12, Verb = "properties", File = Path.GetFullPath(path), Show = 1 }; if (!ShellExecuteExW(ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
}
