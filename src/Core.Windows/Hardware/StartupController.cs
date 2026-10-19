using System.Security.Cryptography;

using System.Text;

using System.Text.Json;

using Core.Domain.Models;

using Microsoft.Extensions.Logging;

using Microsoft.Win32;

namespace Core.Windows.Hardware;



/// <summary>Reversible HKCU Run changes. Never alters services, RunOnce or StartupApproved policy.</summary>

public sealed class StartupController

{

    public const string UserRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string registryPath, backupDirectory;

    private readonly ILogger logger;

    private readonly object gate = new();

    public StartupController(ILogger logger, string? registryPath = null, string? backupDirectory = null)

    {

        this.logger = logger; this.registryPath = registryPath ?? UserRun;

        this.backupDirectory = backupDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CORE", "startup-backups");

    }

    private string BackupFile(string name) => Path.Combine(backupDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(registryPath + "|" + name.ToUpperInvariant()))) + ".json");

    private RegistryKey Root() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    public IReadOnlyList<StartupEntry> Read()

    {

        lock (gate)

        {

            var entries = new List<StartupEntry>(); using var root = Root(); using var key = root.OpenSubKey(registryPath);

            if (key != null) foreach (var name in key.GetValueNames()) { var kind = key.GetValueKind(name); var command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); if (command is string text && kind is RegistryValueKind.String or RegistryValueKind.ExpandString) entries.Add(new(name, text, registryPath, (int)kind, false)); }

            if (Directory.Exists(backupDirectory)) foreach (var file in Directory.EnumerateFiles(backupDirectory, "*.json").Take(4096))

            {

                try { var entry = JsonSerializer.Deserialize<StartupEntry>(File.ReadAllText(file)); if (entry != null && entry.RegistryPath == registryPath && Path.GetFullPath(file) == Path.GetFullPath(BackupFile(entry.Name))) entries.Add(entry with { Disabled = true }); }

                catch (Exception e) when (e is JsonException or IOException) { logger.LogWarning(e, "Could not read startup recovery backup {File}", file); }

            }

            return entries;

        }

    }

    public void SetEnabled(StartupEntry entry, bool enabled)

    {

        lock (gate)

        {

            if (entry.RegistryPath != registryPath || entry.Kind is not (int)RegistryValueKind.String and not (int)RegistryValueKind.ExpandString) throw new InvalidOperationException("Unsupported startup source.");

            using var root = Root(); using var key = root.OpenSubKey(registryPath, writable: true) ?? throw new InvalidOperationException("Startup key is missing or not writable.");

            var existing = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); var file = BackupFile(entry.Name);

            if (enabled)

            {

                if (!entry.Disabled || !File.Exists(file)) throw new InvalidOperationException("No MachinScope recovery backup exists for this entry.");

                var saved = JsonSerializer.Deserialize<StartupEntry>(File.ReadAllText(file)); if (saved == null || saved with { Disabled = entry.Disabled } != entry) throw new InvalidOperationException("Recovery backup changed. Refresh before restoring.");

                if (existing != null && (existing is not string text || text != entry.Command || (int)key.GetValueKind(entry.Name) != entry.Kind)) throw new InvalidOperationException("A different entry now uses this name. MachinScope will not overwrite it.");

                if (existing == null) key.SetValue(entry.Name, entry.Command, (RegistryValueKind)entry.Kind);

                key.Flush();

                if (!Equals(key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), entry.Command) || (int)key.GetValueKind(entry.Name) != entry.Kind) throw new InvalidOperationException("Restored entry could not be verified. Recovery backup retained; inspect current state before retrying.");

                File.Delete(file);

            }

            else

            {

                if (entry.Disabled || existing is not string text || text != entry.Command || (int)key.GetValueKind(entry.Name) != entry.Kind) throw new InvalidOperationException("Startup entry changed. Refresh before disabling.");

                if (File.Exists(file)) throw new InvalidOperationException("A recovery backup already exists. Restore or resolve it first.");

                Directory.CreateDirectory(backupDirectory); var temp = file + ".tmp";

                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(output, entry with { Disabled = true }); output.Flush(true); }

                File.Move(temp, file);

                if (!Equals(key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), entry.Command)) throw new InvalidOperationException("Entry changed during backup; disable refused. Backup retained for recovery.");

                key.DeleteValue(entry.Name, throwOnMissingValue: true); key.Flush();

                if (key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null) throw new InvalidOperationException("Disabled state could not be verified. Recovery backup retained; inspect current state before retrying.");

            }

            logger.LogWarning("AUDIT startup {Action} HKCU entry {Name} at {Path}", enabled ? "restore" : "disable", entry.Name, registryPath);

        }

    }

}

