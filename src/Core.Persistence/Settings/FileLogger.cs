using Microsoft.Extensions.Logging;
namespace Core.Persistence.Settings;
public sealed class FileLoggerProvider(string directory, LogLevel minimum, int retentionDays) : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string logDirectory = directory;
    private readonly LogLevel minimumLevel = minimum;
    private readonly int retention = retentionDays;
    public ILogger CreateLogger(string categoryName) => new LocalLogger(this, categoryName);
    public void Dispose() { }
    private sealed class LocalLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner.minimumLevel;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try { lock (owner.gate) { Directory.CreateDirectory(owner.logDirectory); foreach (var p in Directory.EnumerateFiles(owner.logDirectory, "core-*.jsonl")) if (File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-owner.retention)) File.Delete(p); File.AppendAllText(Path.Combine(owner.logDirectory, $"core-{DateTime.UtcNow:yyyyMMdd}.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, level = level.ToString(), category, eventId = id.Id, message = formatter(state, exception), error = exception?.ToString() }) + Environment.NewLine); } }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

