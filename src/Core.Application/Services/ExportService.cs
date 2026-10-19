using System.Text;
using System.Text.Json;
using Core.Domain.Models;
namespace Core.Application.Services;
public static class ExportService
{
    public static Task WriteAsync(string path, InspectionTable table, CancellationToken token) => WriteRowsAsync(path, table.Title, table.Columns, TableRows.From(table), Enumerable.Range(0, table.Columns.Length).ToArray(), token);
    public static async Task WriteRowsAsync(string path, string title, string[] columns, IEnumerable<InspectionRow> rows, IReadOnlyList<int> visible, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var writer = new Utf8JsonWriter(stream, new() { Indented = true }); writer.WriteStartObject(); writer.WriteString("title", title); writer.WriteStartArray("columns"); foreach (var i in visible) writer.WriteStringValue(columns[i]); writer.WriteEndArray(); writer.WriteStartArray("rows");
            foreach (var row in rows) { token.ThrowIfCancellationRequested(); writer.WriteStartObject(); writer.WriteString("identity", row.Key); writer.WriteStartArray("display"); foreach (var i in visible) writer.WriteStringValue(row.Cells.ElementAtOrDefault(i) ?? ""); writer.WriteEndArray(); writer.WriteStartArray("raw"); foreach (var i in visible) writer.WriteStringValue(row.Raw.ElementAtOrDefault(i) ?? ""); writer.WriteEndArray(); writer.WriteEndObject(); if (writer.BytesPending >= 65536) await writer.FlushAsync(token); }
            writer.WriteEndArray(); writer.WriteEndObject(); await writer.FlushAsync(token);
        }
        else
        {
            static string Cell(string v) => "\"" + (v.Length > 0 && "=+-@\t\r".Contains(v[0]) ? "'" : "") + v.Replace("\"", "\"\"") + "\"";
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true), 65536);
            await writer.WriteLineAsync(string.Join(",", visible.Select(i => Cell(columns[i]))).AsMemory(), token);
            foreach (var row in rows) { token.ThrowIfCancellationRequested(); await writer.WriteLineAsync(string.Join(",", visible.Select(i => Cell(row.Raw.ElementAtOrDefault(i) ?? ""))).AsMemory(), token); }
        }
    }
}
