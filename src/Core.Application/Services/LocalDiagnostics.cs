using System.Text.Json;
namespace Core.Application.Services;

public enum DiagnosticMetric { Refresh, TableReconciliation, ProcessSnapshot, Operation, DeviceEnumeration }
public sealed record DiagnosticSample(DateTimeOffset Time, DiagnosticMetric Metric, double Milliseconds, RefreshState Outcome);

/// <summary>Opt-in, bounded, numeric diagnostics. No paths, identifiers, search queries or exception text.</summary>
public sealed class LocalDiagnostics(int capacity = 256)
{
    private readonly object gate = new();
    private readonly Queue<DiagnosticSample> samples = new();
    private readonly int limit = Math.Clamp(capacity, 1, 4096);
    private bool enabled;
    public bool Enabled { get { lock (gate) return enabled; } set { lock (gate) { enabled = value; if (!value) samples.Clear(); } } }
    public void Record(DiagnosticMetric metric, TimeSpan duration, RefreshState outcome)
    {
        lock (gate) { if (!enabled) return; samples.Enqueue(new(DateTimeOffset.UtcNow, metric, Math.Max(0, duration.TotalMilliseconds), outcome)); while (samples.Count > limit) samples.Dequeue(); }
    }
    public DiagnosticSample[] Snapshot() { lock (gate) return samples.ToArray(); }
    public Task ExportAsync(string path, CancellationToken token) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { schema = 1, redacted = true, samples = Snapshot() }, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }), token);
}
