using Core.Domain.Models;
namespace Core.Application.Services;

/// <summary>Selection owns domain keys, never row indices. Hidden selected rows are retained but not acted on.</summary>
public sealed class TableSelection
{
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private string? anchor;
    public IReadOnlySet<string> Keys => selected;
    public void Reconcile(IEnumerable<string> completeKeys) { selected.IntersectWith(completeKeys); if (anchor != null && !completeKeys.Contains(anchor)) anchor = null; }
    public void ReplaceVisible(IEnumerable<string> visible, IEnumerable<string> chosen) { selected.ExceptWith(visible); selected.UnionWith(chosen); }
    public void Click(IReadOnlyList<string> visible, string key, bool control, bool shift)
    {
        if (!visible.Contains(key)) return;
        if (shift && anchor != null && visible.Contains(anchor))
        {
            if (!control) selected.Clear();
            var a = visible.ToList().IndexOf(anchor); var b = visible.ToList().IndexOf(key);
            foreach (var k in visible.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) selected.Add(k);
        }
        else { if (!control) selected.Clear(); if (control && selected.Contains(key)) selected.Remove(key); else selected.Add(key); anchor = key; }
    }
    public void All(IEnumerable<string> visible) { selected.UnionWith(visible); }
    public void Clear() { selected.Clear(); anchor = null; }
}

public static class TableRows
{
    public static IReadOnlyList<InspectionRow> From(InspectionTable table)
    {
        if (table.Data != null) return table.Data;
        var seen = new Dictionary<string, int>();
        return table.Rows.Select(r =>
        {
            var basis = table.Title + "|" + string.Join("\u001f", r); var occurrence = seen.GetValueOrDefault(basis); seen[basis] = occurrence + 1;
            return new InspectionRow(basis + "|" + occurrence, r, r, table.Columns.Select((name, i) => (name, value: (object?)r.ElementAtOrDefault(i))).GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First().value, StringComparer.OrdinalIgnoreCase));
        }).ToArray();
    }
    public static IReadOnlyList<InspectionRow> FilterSort(IReadOnlyList<InspectionRow> rows, string query, IReadOnlyList<SortColumn> sorts, CancellationToken token)
    {
        var predicate = SearchQuery.Parse(query);
        var matched = new List<InspectionRow>(); foreach (var row in rows) { token.ThrowIfCancellationRequested(); if (predicate.Matches(row)) matched.Add(row); }
        if (sorts.Count > 0) matched.Sort((a, b) => { foreach (var s in sorts) { var result = Compare(a, b, s.Index); if (result != 0) return s.Descending ? -result : result; } return StringComparer.Ordinal.Compare(a.Key, b.Key); });
        return matched;
    }
    public static IReadOnlyList<InspectionRow> KeepExistingOrder(IReadOnlyList<InspectionRow> incoming, IEnumerable<string> previousKeys)
    {
        var remaining = incoming.ToDictionary(r => r.Key, StringComparer.Ordinal); var ordered = new List<InspectionRow>(incoming.Count);
        foreach (var key in previousKeys) if (remaining.Remove(key, out var row)) ordered.Add(row);
        foreach (var row in incoming) if (remaining.Remove(row.Key)) ordered.Add(row);
        return ordered;
    }
    private static int Compare(InspectionRow a, InspectionRow b, int index)
    {
        var av = a.Raw.ElementAtOrDefault(index) ?? ""; var bv = b.Raw.ElementAtOrDefault(index) ?? "";
        return decimal.TryParse(av, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) && decimal.TryParse(bv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ? x.CompareTo(y) : StringComparer.OrdinalIgnoreCase.Compare(av, bv);
    }
    public static string Tsv(IEnumerable<InspectionRow> rows, string[] columns, IReadOnlyList<int> visible, bool headers, bool raw = false)
    {
        static string Escape(string v) => v.Contains('\t') || v.Contains('\r') || v.Contains('\n') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var lines = new List<string>(); if (headers) lines.Add(string.Join("\t", visible.Select(i => Escape(columns[i]))));
        lines.AddRange(rows.Select(r => string.Join("\t", visible.Select(i => Escape((raw ? r.Raw : r.Cells).ElementAtOrDefault(i) ?? "")))));
        return string.Join(Environment.NewLine, lines);
    }
}
