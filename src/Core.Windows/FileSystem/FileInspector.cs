using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Core.Application.Interfaces;
using Core.Domain.Models;
using Core.Windows.Native;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
namespace Core.Windows.FileSystem;
public sealed class FileInspector(ILogger<FileInspector> logger) : IFileInspector
{
    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private static string SafeRoot(string root)
    {
        var full = Path.GetFullPath(root); if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Choose a physical directory; reparse-point roots are skipped."); return full;
    }
    // Depth-first streaming walk owns one enumerator per depth, not one entry per file.
    private static IEnumerable<(FileSystemInfo Entry, int Depth)> Walk(string root, Action<string, Exception> error, Action reparse, CancellationToken token)
    {
        var stack = new Stack<(IEnumerator<FileSystemInfo> Items, int Depth)>();
        void Push(string path, int depth) { try { stack.Push((new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator(), depth)); } catch (Exception e) when (Expected(e)) { error(path, e); } }
        Push(root, 0);
        try
        {
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested(); var current = stack.Peek(); FileSystemInfo entry;
                try { if (!current.Items.MoveNext()) { stack.Pop().Items.Dispose(); continue; } entry = current.Items.Current; }
                catch (Exception e) when (Expected(e)) { stack.Pop().Items.Dispose(); error(root, e); continue; }
                FileAttributes attrs; try { attrs = entry.Attributes; } catch (Exception e) when (Expected(e)) { error(entry.FullName, e); continue; }
                if ((attrs & FileAttributes.ReparsePoint) != 0) { reparse(); continue; }
                yield return (entry, current.Depth);
                if ((attrs & FileAttributes.Directory) != 0) { if (current.Depth >= 256) error(entry.FullName, new IOException("Maximum scan depth 256 reached.")); else Push(entry.FullName, current.Depth + 1); }
            }
        }
        finally { while (stack.Count > 0) stack.Pop().Items.Dispose(); }
    }
    public InspectionTable List(string path, string filter, CancellationToken token)
    {
        var rows = new List<string[]>(); var root = SafeRoot(path); var truncated = false;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested(); if (!entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            try { rows.Add([entry.Name, entry is DirectoryInfo ? "Directory" : "File", entry is FileInfo f ? f.Length.ToString() : "—", entry.LastWriteTime.ToString("O"), entry.Attributes.ToString(), entry.FullName, entry.CreationTime.ToString("O")]); }
            catch (Exception e) when (Expected(e)) { rows.Add([entry.Name, "Unavailable", "—", "—", e.Message, entry.FullName, "Unavailable"]); }
            if (rows.Count >= 20000) { truncated = true; break; }
        }
        var data = rows.Select(r => new InspectionRow(r[5], r, r, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["name"] = r[0], ["ext"] = Path.GetExtension(r[0]).TrimStart('.'), ["type"] = r[1], ["size"] = r[2], ["modified"] = r[3], ["created"] = r[6], ["path"] = r[5], ["hidden"] = r[4].Contains("Hidden") ? "true" : "false" })).ToArray();
        return new("Files", ["Name", "Kind", "Size B", "Modified", "Attributes", "Path", "Created"], rows, truncated ? "Display capped at 20,000 entries. Apply a name/extension search to narrow results. Scanner streams the full directory tree." : "Double-click a directory to navigate. Reparse points are not followed by scans. Sizes are logical bytes, not allocated disk space.", data);
    }
    public Task<ScanReport> ScanAsync(string root, bool includeHidden, IProgress<string>? progress, CancellationToken token) => Task.Run(() =>
    {
        root = SafeRoot(root); var clock = Stopwatch.StartNew(); long bytes = 0, files = 0, folders = 0, skipped = 0, errorCount = 0; var maxDepth = 0;
        var issues = new List<ScanIssue>(); var topFiles = new PriorityQueue<SizedPath, long>(); var topDirs = new PriorityQueue<SizedPath, long>();
        var extensions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); var active = new List<(string Path, long Size)> { (root, 0) };
        static void Top(PriorityQueue<SizedPath, long> q, string p, long b) { q.Enqueue(new(p, b), b); if (q.Count > 100) q.Dequeue(); }
        void Error(string p, Exception e) { errorCount++; if (issues.Count < 1000) issues.Add(new(p, e.Message)); }
        void CloseDirectory() { var last = active[^1]; active.RemoveAt(active.Count - 1); Top(topDirs, last.Path, last.Size); if (active.Count > 0) { var parent = active[^1]; active[^1] = (parent.Path, parent.Size + last.Size); } }
        foreach (var (entry, depth) in Walk(root, Error, () => skipped++, token))
        {
            while (active.Count > depth + 1) CloseDirectory(); maxDepth = Math.Max(maxDepth, depth + 1);
            if (entry is DirectoryInfo) { folders++; active.Add((entry.FullName, 0)); continue; }
            if (!includeHidden && (entry.Attributes & FileAttributes.Hidden) != 0) continue;
            try { var file = (FileInfo)entry; var size = file.Length; bytes += size; files++; var dir = active[^1]; active[^1] = (dir.Path, dir.Size + size); Top(topFiles, file.FullName, size); var ext = file.Extension.ToLowerInvariant(); if (ext.Length == 0) ext = "(none)"; if (!extensions.ContainsKey(ext) && extensions.Count >= 4096) ext = "(other)"; extensions[ext] = extensions.GetValueOrDefault(ext) + size; if (files % 500 == 0) progress?.Report($"{files:N0} files · {Calculations.Bytes(bytes)} · {entry.FullName}"); }
            catch (Exception e) when (Expected(e)) { Error(entry.FullName, e); }
        }
        while (active.Count > 0) CloseDirectory();
        return new ScanReport(root, bytes, files, folders, maxDepth, skipped, errorCount, issues, topFiles.UnorderedItems.Select(i => i.Element).OrderByDescending(i => i.Bytes).ToArray(), topDirs.UnorderedItems.Select(i => i.Element).OrderByDescending(i => i.Bytes).ToArray(), extensions, clock.Elapsed);
    }, token);
    private static string Identity(FileStream stream)
    {
        if (!NativeMethods.GetFileInformationByHandle(stream.SafeFileHandle, out var info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    }
    private static async Task<bool> EqualAsync(string first, string second, CancellationToken token)
    {
        using var a = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true); using var b = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (a.Length != b.Length) return false; var ba = new byte[65536]; var bb = new byte[65536];
        while (true) { token.ThrowIfCancellationRequested(); var na = await a.ReadAsync(ba, token); if (na == 0) return b.Position == b.Length; await b.ReadExactlyAsync(bb.AsMemory(0, na), token); if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, na))) return false; }
    }
    public Task<DuplicateReport> DuplicatesAsync(string root, IProgress<string>? progress, CancellationToken token) => Task.Run(async () =>
    {
        root = SafeRoot(root); var database = Path.Combine(Path.GetTempPath(), $"core-scan-{Guid.NewGuid():N}.db"); long errors = 0, hardLinks = 0, count = 0, reclaimable = 0; var result = new List<DuplicateGroup>(); bool truncated = false;
        try
        {
            using var db = new SqliteConnection($"Data Source={database};Pooling=False"); db.Open();
            using (var init = db.CreateCommand()) { init.CommandText = "CREATE TABLE files(path TEXT PRIMARY KEY, size INTEGER, modified INTEGER, identity TEXT UNIQUE, hash TEXT); CREATE INDEX sizes ON files(size);"; init.ExecuteNonQuery(); }
            using (var transaction = db.BeginTransaction())
            using (var insert = db.CreateCommand())
            {
                insert.Transaction = transaction; insert.CommandText = "INSERT OR IGNORE INTO files VALUES($p,$s,$m,$i,NULL)";
                var pp = insert.Parameters.Add("$p", SqliteType.Text); var ps = insert.Parameters.Add("$s", SqliteType.Integer); var pm = insert.Parameters.Add("$m", SqliteType.Integer); var pi = insert.Parameters.Add("$i", SqliteType.Text);
                foreach (var (entry, _) in Walk(root, (_, _) => errors++, () => { }, token))
                {
                    if (entry is not FileInfo f) continue;
                    try { using var stream = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length == 0) continue; pp.Value = f.FullName; ps.Value = stream.Length; pm.Value = f.LastWriteTimeUtc.Ticks; pi.Value = Identity(stream); if (insert.ExecuteNonQuery() == 0) hardLinks++; count++; if (count % 1000 == 0) progress?.Report($"Indexed {count:N0} files (disk-backed candidate index)"); }
                    catch (Exception e) when (Expected(e) || e is System.ComponentModel.Win32Exception) { errors++; }
                }
                transaction.Commit();
            }
            // Full cryptographic hashes only for equal-size candidates, streamed from disk.
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT path,size,modified,identity FROM files WHERE size IN (SELECT size FROM files GROUP BY size HAVING COUNT(*)>1)";
                using var reader = query.ExecuteReader(); long hashed = 0;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested(); var path = reader.GetString(0);
                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                        if (stream.Length != reader.GetInt64(1) || File.GetLastWriteTimeUtc(path).Ticks != reader.GetInt64(2) || Identity(stream) != reader.GetString(3)) { errors++; continue; }
                        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                        if (File.GetLastWriteTimeUtc(path).Ticks != reader.GetInt64(2)) { errors++; continue; }
                        using var update = db.CreateCommand(); update.CommandText = "UPDATE files SET hash=$h WHERE path=$p"; update.Parameters.AddWithValue("$h", hash); update.Parameters.AddWithValue("$p", path); update.ExecuteNonQuery();
                        if (++hashed % 50 == 0) progress?.Report($"Hashed {hashed:N0} candidates · {path}");
                    }
                    catch (Exception e) when (Expected(e) || e is System.ComponentModel.Win32Exception) { errors++; }
                }
            }
            using var groups = db.CreateCommand(); groups.CommandText = "SELECT size,hash,COUNT(*) FROM files WHERE hash IS NOT NULL GROUP BY size,hash HAVING COUNT(*)>1";
            using var groupReader = groups.ExecuteReader();
            while (groupReader.Read())
            {
                token.ThrowIfCancellationRequested(); var size = groupReader.GetInt64(0); if (groupReader.GetInt64(2) > 10000) truncated = true; using var paths = db.CreateCommand(); paths.CommandText = "SELECT path,modified,identity FROM files WHERE size=$s AND hash=$h LIMIT 10001"; paths.Parameters.AddWithValue("$s", size); paths.Parameters.AddWithValue("$h", groupReader.GetString(1));
                using var pathReader = paths.ExecuteReader(); var verified = new List<string>();
                while (pathReader.Read())
                {
                    if (verified.Count == 10000) { truncated = true; break; } var path = pathReader.GetString(0);
                    try { using (var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { if (check.Length != size || File.GetLastWriteTimeUtc(path).Ticks != pathReader.GetInt64(1) || Identity(check) != pathReader.GetString(2)) { errors++; continue; } } if (verified.Count == 0 || await EqualAsync(verified[0], path, token)) verified.Add(path); else errors++; }
                    catch (Exception e) when (Expected(e) || e is System.ComponentModel.Win32Exception) { errors++; }
                }
                if (verified.Count > 1) { result.Add(new(size, verified)); reclaimable += size * (verified.Count - 1); }
                if (result.Count >= 1000) { truncated = true; break; }
            }
            return new DuplicateReport(result, reclaimable, hardLinks, errors, truncated);
        }
        finally { try { File.Delete(database); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { logger.LogWarning(e, "Temporary duplicate index cleanup failed: {Database}", database); } }
    }, token);
    private static string ValidateOperation(string source)
    {
        var path = Path.GetFullPath(source); var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var protectedRoot in new[] { windows, programFiles, programFilesX86, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) })
            if (!string.IsNullOrEmpty(protectedRoot) && (path.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) || path.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("MachinScope refuses file changes in system and application directories.");
        if (Directory.Exists(path)) throw new NotSupportedException("Management operations support individual files only.");
        if (!File.Exists(path)) throw new FileNotFoundException("File no longer exists.", path);
        var current = new FileInfo(path) as FileSystemInfo; while (current != null) { if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("File operations through reparse points are blocked."); current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent; }
        return path;
    }
    internal static Core.Domain.Models.ActionDecision OperationDecision(string path)
    {
        try { ValidateOperation(path); return Core.Domain.Models.ActionDecision.Permit; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return Core.Domain.Models.ActionDecision.Deny(e.Message); }
    }
    public void Copy(string source, string destination) { source = ValidateOperation(source); ValidateDestination(destination); File.Copy(source, destination, false); logger.LogWarning("AUDIT copy {Source} to {Destination}", source, destination); }
    public void Move(string source, string destination) { source = ValidateOperation(source); ValidateDestination(destination); File.Move(source, destination, false); logger.LogWarning("AUDIT move {Source} to {Destination}", source, destination); }
    private static void ValidateDestination(string destination)
    {
        var path = Path.GetFullPath(destination); var parent = Path.GetDirectoryName(path) ?? throw new ArgumentException("Destination requires a directory.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        foreach (var protectedRoot in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }) if (!string.IsNullOrEmpty(protectedRoot) && (parent.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) || parent.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("Destination is a protected directory.");
        for (var d = new DirectoryInfo(parent); d != null; d = d.Parent) if ((d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Destination traverses a reparse point.");
    }
    public void Recycle(string path) { path = ValidateOperation(path); Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException); logger.LogWarning("AUDIT recycle {Path}", path); }
}

