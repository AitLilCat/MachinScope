namespace Core.IntegrationTests;
internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--phase75-diagnostics") { await Phase75Diagnostics.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--storage-benchmark") { await NativeBenchmarks.Run(args[1]); return; }
        if (!args.SequenceEqual(new[] { "--process-fixture" })) return;
        Console.WriteLine("CORE control fixture ready");
        await Task.Delay(TimeSpan.FromMinutes(1));
    }
}
