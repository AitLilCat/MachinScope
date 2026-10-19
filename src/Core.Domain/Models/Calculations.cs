namespace Core.Domain.Models;

public static class Calculations
{
    public static double? Cpu(ulong idle, ulong kernel, ulong user, ulong oldIdle, ulong oldKernel, ulong oldUser)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        var total = (kernel - oldKernel) + (user - oldUser);
        return total == 0 ? null : Math.Clamp(100d * (total - Math.Min(total, idle - oldIdle)) / total, 0, 100);
    }
    public static double? Rate(long current, long previous, double seconds) => seconds <= 0 || current < previous ? null : (current - previous) / seconds;
    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var i = 0; while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.##} {units[i]}";
    }
}
public sealed class RollingBuffer<T>(int capacity)
{
    private readonly Queue<T> values = new();
    public void Add(T value) { if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity)); if (values.Count == capacity) values.Dequeue(); values.Enqueue(value); }
    public IReadOnlyList<T> Values => values.ToArray();
}
