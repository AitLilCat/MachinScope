using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Application.Services;
using Core.Windows.Hardware;
namespace Core.Desktop;

public partial class App
{
    // Opt-in packaging probe. It initializes WinUI/XAML normally, writes one requested report and creates no inspectors/window.
    private static async Task<bool> VerifyPortableBuild()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("--verify-build", StringComparer.Ordinal)) return false;
        var reportIndex = Array.IndexOf(args, "--report");
        if (reportIndex < 0 || reportIndex + 1 >= args.Length) throw new ArgumentException("--verify-build requires --report <absolute-file-path>");
        var path = Path.GetFullPath(args[reportIndex + 1]); var platform = new WindowsPlatform();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var administration = new WindowsAdministrationTools(platform, platform);
        var tools = await administration.InspectAsync(false, timeout.Token);
        if (NavigationRegistry.Routes.Select(r => r.Id).Distinct().Count() != NavigationRegistry.Routes.Count) throw new InvalidOperationException("Duplicate route registration");
        File.WriteAllText(path, JsonSerializer.Serialize(new {
            schema = 2, verified = true, scope = "WinUI/XAML bootstrap, platform, metadata and native tool availability; no MainWindow or native utility launch; no interactive UI acceptance",
            framework = RuntimeInformation.FrameworkDescription, runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(),
            platform = platform.Platform.Kind.ToString(), architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            routes = NavigationRegistry.Routes.Count, tools = ToolRegistry.Tools.Count,
            host = platform.Platform, descriptions = InspectionDescriptions.Tabs.Count, administration = tools,
            capabilities = platform.Capabilities.Select(c => new { c.Id, status = c.Status.ToString() })
        }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
