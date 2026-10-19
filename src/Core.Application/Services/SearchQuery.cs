using System.Globalization;
using System.Text.RegularExpressions;
using Core.Domain.Models;
namespace Core.Application.Services;

public sealed class SearchQuery
{
    private readonly List<Func<InspectionRow, bool>> terms = new();
    private SearchQuery() { }
    public static SearchQuery Parse(string input, DateTimeOffset? referenceTime = null)
    {
        var query = new SearchQuery();
        var now = referenceTime ?? DateTimeOffset.Now;
        // Quoted terms support whitespace. Backslashes are literal (Windows paths).
        foreach (Match m in Regex.Matches(input, "(?:[^\\s\"]+|\"[^\"]*\")+"))
        {
            var text = m.Value.Replace("\"", "");
            var term = Regex.Match(text, "^([A-Za-z][A-Za-z0-9_]*)(>=|<=|>|<|=|:)(.*)$");
            if (!term.Success) { query.terms.Add(r => r.Cells.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase))); continue; }
            var field = term.Groups[1].Value.ToLowerInvariant(); var op = term.Groups[2].Value; var expected = term.Groups[3].Value;
            if (expected.Length == 0) throw new FormatException($"Missing value for {field}.");
            if (op is ">" or "<" or ">=" or "<=")
            {
                if (field is "modified" or "created" or "accessed")
                {
                    var age = Regex.Match(expected, "^([0-9]+)(d|h|m|s)$", RegexOptions.IgnoreCase);
                    double? seconds = age.Success ? double.Parse(age.Groups[1].Value, CultureInfo.InvariantCulture) * (age.Groups[2].Value.ToLowerInvariant() switch { "d" => 86400, "h" => 3600, "m" => 60, _ => 1 }) : null;
                    DateTimeOffset date = default;
                    if (!seconds.HasValue && !DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)) throw new FormatException("Use an ISO date or an age such as 30d for " + field);
                    query.terms.Add(r =>
                    {
                        if (!r.Fields.TryGetValue(field, out var value) || !DateTimeOffset.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actual)) return false;
                        var comparison = seconds.HasValue ? (now - actual).TotalSeconds.CompareTo(seconds.Value) : actual.CompareTo(date);
                        return op switch { ">" => comparison > 0, "<" => comparison < 0, ">=" => comparison >= 0, _ => comparison <= 0 };
                    });
                    continue;
                }
                if (!Number(expected, out var limit)) throw new FormatException($"Invalid numeric value: {expected}");
                query.terms.Add(r => r.Fields.TryGetValue(field, out var value) && value != null && Number(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", out var actual) && (op switch { ">" => actual > limit, "<" => actual < limit, ">=" => actual >= limit, _ => actual <= limit }));
            }
            else query.terms.Add(r =>
            {
                IEnumerable<object?> values = field switch { "port" => new[] { r.Fields.GetValueOrDefault("localport"), r.Fields.GetValueOrDefault("remoteport") }, "remote" when int.TryParse(expected, out _) => new[] { r.Fields.GetValueOrDefault("remoteport") }, _ => new[] { r.Fields.GetValueOrDefault(field) } };
                return values.Any(v => v != null && (op == "=" ? Convert.ToString(v, CultureInfo.InvariantCulture)!.Equals(expected, StringComparison.OrdinalIgnoreCase) : Convert.ToString(v, CultureInfo.InvariantCulture)!.Contains(expected, StringComparison.OrdinalIgnoreCase)));
            });
        }
        return query;
    }
    public bool Matches(InspectionRow row) => terms.All(t => t(row));
    public static bool Number(string input, out decimal value)
    {
        value = 0; var m = Regex.Match(input.Trim(), "^([+-]?[0-9]+(?:[.,][0-9]+)?)\\s*(B|KiB|MiB|GiB|TiB|KB|MB|GB|%|/s|KiB/s|MiB/s)?$", RegexOptions.IgnoreCase);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        var unit = m.Groups[2].Value.ToUpperInvariant().Replace("/S", "");
        value *= unit switch { "KIB" => 1024m, "MIB" => 1048576m, "GIB" => 1073741824m, "TIB" => 1099511627776m, "KB" => 1000m, "MB" => 1000000m, "GB" => 1000000000m, _ => 1m }; return true;
    }
}
