namespace Core.Application.Services;
public static class GraphMath
{
    public static double RateCeiling(double peak)
    {
        if (!double.IsFinite(peak) || peak <= 1024) return 1024;
        return Math.Pow(2, Math.Ceiling(Math.Log2(peak)));
    }
    public static IReadOnlyList<IReadOnlyList<(double X, double Value)>> Segments(IEnumerable<(DateTimeOffset Time, double? Value)> samples, DateTimeOffset now, int seconds, double maximumGapSeconds)
    {
        var result = new List<IReadOnlyList<(double X, double Value)>>(); List<(double X, double Value)>? segment = null; DateTimeOffset? previous = null;
        foreach (var sample in samples.OrderBy(s => s.Time))
        {
            var age = (now - sample.Time).TotalSeconds; if (age < 0 || age > seconds) continue;
            if (sample.Value == null || !double.IsFinite(sample.Value.Value) || previous.HasValue && (sample.Time - previous.Value).TotalSeconds > maximumGapSeconds) segment = null;
            if (sample.Value is { } value && double.IsFinite(value)) { if (segment == null) { segment = new(); result.Add(segment); } segment.Add((1 - age / seconds, value)); }
            previous = sample.Time;
        }
        return result;
    }
}
