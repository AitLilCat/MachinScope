using Core.Application.Interfaces;

using Core.Domain.Models;

using System.Text.Json;

namespace Core.Persistence.Settings;

public sealed class PreferencesStore : IPreferencesStore

{

    private readonly object gate = new();

    public string DataDirectory { get; }

    private string FilePath => Path.Combine(DataDirectory, "settings.json");

    public PreferencesStore(string? directory = null) { DataDirectory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CORE"); Directory.CreateDirectory(DataDirectory); }

    public Preferences Load()

    {

        lock (gate) return LoadLocked();

    }

    private Preferences LoadLocked()

    {

        if (!File.Exists(FilePath)) return new();

        try { var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); return Validate(p); }

        catch (JsonException)

        {

            File.Copy(FilePath, FilePath + ".corrupt", true);

            if (!File.Exists(FilePath + ".bak")) return new();

            try { var recovered = Validate(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath + ".bak")) ?? new()); WriteSettings(recovered, false); return recovered; }

            catch (JsonException) { return new(); }

        }

    }

    private static Preferences Validate(Preferences p) => p with { Navigation = (p.Navigation ?? new()) with { LastDestination = p.Navigation?.LastDestination is { Length: > 0 and <= 128 } id ? id : "core.overview", ExpandedCategories = (p.Navigation?.ExpandedCategories ?? []).Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 64).Distinct().Take(16).ToArray() }, RefreshSeconds = p.RefreshSeconds is 1 or 2 or 5 ? p.RefreshSeconds : 2, Theme = p.Theme is "System" or "Dark" or "Light" ? p.Theme : "System", LogRetentionDays = Math.Clamp(p.LogRetentionDays, 1, 90), HistorySeconds = p.HistorySeconds is 60 or 300 or 900 ? p.HistorySeconds : 60, Tables = (p.Tables ?? new()).Take(64).ToDictionary(x => x.Key, x => (x.Value ?? []).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name)).GroupBy(c => c.Name).Select(g => g.First()).Take(64).Select(c => c with { Width = double.IsFinite(c.Width) ? Math.Clamp(c.Width, 60, 4000) : 120 }).ToArray()) };

    public void Save(Preferences preferences)

    {

        lock (gate)

        {

            WriteSettings(preferences, true);

        }

    }

    private void WriteSettings(Preferences preferences, bool backup)

    {

            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try

            {

                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))

                {

                    JsonSerializer.Serialize(stream, Validate(preferences), new JsonSerializerOptions { WriteIndented = true });

                    stream.Flush(true);

                }

                if (backup && File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);

                else File.Move(temp, FilePath, true);

            }

            finally { if (File.Exists(temp)) File.Delete(temp); }

    }

}

