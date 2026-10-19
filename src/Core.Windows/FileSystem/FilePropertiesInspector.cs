using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Core.Application.Interfaces;
using Core.Domain.Models;
using Core.Windows.Native;
using Microsoft.Win32.SafeHandles;
namespace Core.Windows.FileSystem;

public sealed class FilePropertiesInspector : IFilePropertiesInspector
{
    [StructLayout(LayoutKind.Sequential)] private struct StandardInfo { public long Allocation, Length; public uint Links; public byte DeletePending, Directory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out StandardInfo info, uint size);
    private static string Identity(NativeMethods.FileInformation info) => $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    private static DateTimeOffset Time(System.Runtime.InteropServices.ComTypes.FILETIME time) => DateTime.FromFileTimeUtc(((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime);
    private static FileTarget Target(string path, NativeMethods.FileInformation info) => new(path, Identity(info), (info.Attributes & (uint)FileAttributes.Directory) != 0 ? null : ((long)info.SizeHigh << 32) | info.SizeLow, Time(info.Write), (info.Attributes & (uint)FileAttributes.Directory) != 0);
    public Task<FileDetails> DetailsAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); path = Path.GetFullPath(path);
        // OPEN_REPARSE_POINT inspects the link object rather than silently following it.
        using var handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !NativeMethods.GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var target = Target(path, info);
        static InspectionTable Table(string title, IEnumerable<string[]> rows, string? notice = null) => new(title, ["Property", "Value"], rows.ToArray(), notice);
        var standardAvailable = GetFileInformationByHandleEx(handle, 1, out var standard, (uint)Marshal.SizeOf<StandardInfo>());
        var allocated = standardAvailable && !target.Directory ? standard.Allocation.ToString() : "Unavailable";
        var general = Table("General", [["Name", Path.GetFileName(path)], ["Location", path], ["Type", target.Directory ? "Directory" : Path.GetExtension(path)], ["Logical size B", target.Length?.ToString() ?? "Use folder scan"], ["Allocated size B", allocated]]);
        var dates = Table("Dates", [["Created (UTC)", Time(info.Creation).ToString("O")], ["Modified (UTC)", Time(info.Write).ToString("O")], ["Accessed (UTC)", Time(info.Access).ToString("O")]], "Timestamp precision and last-access updates depend on the filesystem and Windows configuration.");
        var security = new List<string[]>();
        try
        {
            FileSystemSecurity acl = target.Directory ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access) : new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            security.Add(["Owner", acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? "Unavailable"]);
            security.Add(["Access descriptor (SDDL)", acl.GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access)]);
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier))) security.Add([rule.IdentityReference.Value, $"{rule.AccessControlType} · {rule.FileSystemRights} · inherited={rule.IsInherited}"]);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { security.Add(["ACL", "Unavailable: " + e.Message]); }
        // ACL access is path-based; reject a replacement detected after reading it.
        using var current = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (current.IsInvalid || !NativeMethods.GetFileInformationByHandle(current, out var after) || Identity(after) != target.Identity) throw new IOException("File was replaced during property inspection.");
        var advanced = Table("Advanced", [["File identity", target.Identity], ["Hard links", info.Links.ToString()], ["Attributes", ((FileAttributes)info.Attributes).ToString()], ["Delete pending", standardAvailable ? standard.DeletePending.ToString() : "Unavailable"], ["Alternate streams", "Not enumerated by this provider"]], "File ID is scoped to its volume. Reparse objects are inspected without following their target.");
        var usage = Table("Usage", [["Directory totals", target.Directory ? "Request Scan folder from the context menu" : "Not applicable"], ["Allocated size source", "GetFileInformationByHandleEx / FileStandardInfo"]], "Ordinary properties do not recurse directories or read file contents.");
        return new FileDetails(target, [general, dates, Table("Security", security, "Owner SID and actual ACL rules; no permissions are modified."), Table("Checksums", [["SHA-256 / SHA-512", target.Directory ? "Unavailable for directories" : "Use the checksum buttons to read the file explicitly"]]), advanced, usage, Table("Operations", [["File operation policy", FileInspector.OperationDecision(path).Reason], ["Copy / move / rename / recycle", "Available for policy-permitted individual files through the reviewed context workflow"], ["Directory mutations / bulk operations", "Unavailable in this release"], ["Permissions", "The filesystem revalidates effective access at execution; ACL display is not an access grant"], ["Undo", "No automatic rollback is claimed"]])]);
    }, token);
    public async Task<string> HashAsync(FileTarget target, string algorithm, CancellationToken token)
    {
        if (target.Directory) throw new NotSupportedException("Hashing requires a regular file.");
        if (algorithm is not "SHA-256" and not "SHA-512") throw new ArgumentException("Unsupported checksum algorithm.", nameof(algorithm));
        var attrs = File.GetAttributes(target.Path); if ((attrs & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException("Checksum actions do not follow reparse points.");
        await using var file = new FileStream(target.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!NativeMethods.GetFileInformationByHandle(file.SafeFileHandle, out var before) || Target(target.Path, before) != target) throw new IOException("File identity, size or timestamp changed. Refresh properties before hashing.");
        var bytes = algorithm == "SHA-256" ? await SHA256.HashDataAsync(file, token) : await SHA512.HashDataAsync(file, token);
        if (!NativeMethods.GetFileInformationByHandle(file.SafeFileHandle, out var after) || Target(target.Path, after) != target) throw new IOException("File changed during hashing; checksum discarded.");
        return Convert.ToHexString(bytes);
    }
}
