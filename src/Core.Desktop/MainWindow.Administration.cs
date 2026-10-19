using Core.Application.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Core.Desktop;

public sealed partial class MainWindow
{
    private AdministrationGroup? administrationGroup;
    private void OpenAdministrationCatalog(AdministrationGroup? group = null) {
        administrationGroup = group; NavigateTo(NavigationDestination.Tools);
    }
    private void AddCatalogShortcut(StackPanel stack, AdministrationGroup group) {
        var button = new Button { Content = AdministrationCatalog.GroupName(group) + "…" };
        button.Click += (_, _) => OpenAdministrationCatalog(group); stack.Children.Add(button);
    }
    private void ShowAdministrationCatalog(StackPanel stack)
    {
        Notice.Text = "Windows God Mode · a catalog of native entry points and MachinScope inspectors. Windows permissions and UAC apply; opening a utility does not grant privileges.";
        var search = new TextBox { PlaceholderText = "Search administration tools…", Width = 360 };
        var groups = new ComboBox { ItemsSource = new[] { "All groups" }.Concat(Enum.GetValues<AdministrationGroup>().Select(AdministrationCatalog.GroupName)).ToArray(), SelectedIndex = administrationGroup is { } group ? (int)group + 1 : 0 };
        Toolbar.Children.Add(search); Toolbar.Children.Add(groups);
        var toolsHost = new StackPanel { Spacing = 12 }; stack.Children.Add(toolsHost);
        IReadOnlyDictionary<string, ToolAvailability> availability = new Dictionary<string, ToolAvailability>();
        void Render() {
            if (closing || currentDestination != NavigationDestination.Tools) return;
            toolsHost.Children.Clear();
            var tools = AdministrationCatalog.Filter(vm.Capabilities.Platform.Kind, search.Text, administrationGroup);
            if (tools.Count == 0) toolsHost.Children.Add(new TextBlock { Text = "No tools match the current search." });
            foreach (var category in tools.GroupBy(t => t.Group)) {
                toolsHost.Children.Add(new TextBlock { Text = AdministrationCatalog.GroupName(category.Key), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var tool in category) {
                    var state = availability.GetValueOrDefault(tool.Id);
                    var panel = new StackPanel { Spacing = 3, Margin = new(0, 0, 0, 5) };
                    panel.Children.Add(new TextBlock { Text = tool.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                    panel.Children.Add(new TextBlock { Text = tool.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
                    panel.Children.Add(new TextBlock { Text = $"{tool.Platform?.ToString() ?? "Shared"} · {tool.Privilege} · {state?.State.ToString() ?? "Checking availability"}\n{state?.Explanation ?? "On-demand checks are running."}", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
                    var open = new Button { Content = tool.Destination != null ? "Inspect in MachinScope…" : "Open Windows utility…", IsEnabled = state?.CanOpen == true };
                    open.Click += async (_, _) => { try {
                        if (tool.Destination is { } destination) NavigateTo(destination);
                        else await Work(async token => { await vm.Administration.OpenAsync(tool.Id, token); vm.Status = "Opened native Windows utility: " + tool.Name; });
                    } catch (Exception e) { vm.Error(e); } };
                    panel.Children.Add(open); toolsHost.Children.Add(panel);
                }
            }
        }
        Task Load(bool force) => Work(async token => {
            var states = await vm.Administration.InspectAsync(force, token); token.ThrowIfCancellationRequested();
            availability = states.ToDictionary(s => s.Id); Render();
        });
        search.TextChanged += (_, _) => Render();
        groups.SelectionChanged += (_, _) => { administrationGroup = groups.SelectedIndex > 0 ? (AdministrationGroup)(groups.SelectedIndex - 1) : null; Render(); };
        AddButton("Refresh tools", () => Load(true)); Render(); _ = Load(false);
    }
}
