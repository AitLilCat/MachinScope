using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Core.Desktop;

public sealed partial class MainWindow
{
    private readonly AsyncSnapshotCache<InspectionTable> inventoryCache = new(TimeSpan.FromMinutes(1));
    private readonly AsyncSnapshotCache<InspectionTable> adapterCache = new(TimeSpan.FromSeconds(2));
    private readonly AsyncSnapshotCache<ConnectionSnapshot> endpointCache = new(TimeSpan.FromSeconds(2));
    private sealed record ConnectionSnapshot(IReadOnlyList<ConnectionRow> Rows);
    private DeviceSnapshot? lastDevices;
    private readonly Dictionary<string, TextBlock> summarySections = new();
    private OverviewSnapshot CurrentOverview() => new(vm.LastSample, vm.Processes, inventoryCache.Last, vm.Storage.Last?.Inventory,
        lastDevices, adapterCache.Last, endpointCache.Last?.Rows.Count);
    private void ShowHomepage(NavigationRoute route)
    {
        DataTable.Visibility = Visibility.Collapsed; FilterBar.Visibility = Visibility.Collapsed;
        Notice.Text = "Read-only summaries · collection is bounded and reused · actions run only when requested.";
        summarySections.Clear();
        if (route.Destination is NavigationDestination.CoreOverview or NavigationDestination.ActivityOverview or NavigationDestination.HardwareOverview or NavigationDestination.NetworkOverview or NavigationDestination.ConfigurationOverview) AddButton("Refresh summary", ManualRefresh);
        var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var section in OverviewSections.Build(route.Destination, CurrentOverview()))
        {
            AddShortcut(stack, NavigationRegistry.Get(section.Destination), section.Name + "…");
            var text = new TextBlock { Text = section.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 5) };
            summarySections.Add(section.Name, text); stack.Children.Add(text);
        }
        if (route.Destination == NavigationDestination.Tools) {
            ShowAdministrationCatalog(stack);
        }
        if (route.Destination == NavigationDestination.Diagnostics)
        {
            stack.Children.Add(new TextBlock { Text = "DNS · Reverse DNS · Ping · TCP · Traceroute\nRequests contact only the host specified in the review dialog. Packet capture, per-process bandwidth, connection byte counts and routing-table inspection are unsupported.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var button = new Button { Content = "Run network diagnostic…" };
            button.Click += async (_, _) => { try { await Diagnose(); } catch (Exception e) { vm.Error(e); } }; stack.Children.Add(button);
        }
        if (route.Destination == NavigationDestination.Maintenance)
            stack.Children.Add(new TextBlock { Text = "MachinScope logs are local. Log retention and diagnostic-report export are in MachinScope Preferences. Native Windows maintenance and administration utilities are available through Advanced Tools. MachinScope does not directly repair Windows or manage scheduled tasks.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        if (route.Destination == NavigationDestination.Maintenance) AddCatalogShortcut(stack, AdministrationGroup.StorageUtilities);
        if (route.Category is { } category && route.Destination != NavigationDestination.Tools)
        {
            var prefixes = category switch { NavigationCategory.Activity => new[] { "process.", "performance.", "startup.", "services." }, NavigationCategory.Hardware => ["disk.", "files.", "devices.", "images."], NavigationCategory.Network => ["network."], _ => ["platform.", "os.", "firewall."] };
            foreach (var capability in vm.Capabilities.Capabilities.Where(c => prefixes.Any(p => c.Id.StartsWith(p, StringComparison.Ordinal))))
                stack.Children.Add(new TextBlock { Text = $"{capability.Id} · {capability.Status} · {capability.Source}\n{capability.Reason}", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        settingsHost = new ScrollViewer { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(settingsHost, 1); Grid.SetRow(settingsHost, 3); ContentArea.Children.Add(settingsHost);
        vm.Status = "Ready · summary observations may be cached; detailed identity checks remain fresh.";
    }
    private void UpdateHomepageSummary()
    {
        if (page != "Category") return;
        foreach (var section in OverviewSections.Build(routes.Selected, CurrentOverview())) if (summarySections.TryGetValue(section.Name, out var text)) text.Text = section.Detail;
    }
    private async Task LoadHomepage(NavigationDestination destination, long generation, bool force, CancellationToken token)
    {
        if (destination is NavigationDestination.CoreOverview or NavigationDestination.ConfigurationOverview)
            await inventoryCache.ReadAsync(vm.Inspector.OverviewAsync, force, token);
        if (destination is NavigationDestination.CoreOverview or NavigationDestination.NetworkOverview)
        {
            await adapterCache.ReadAsync(ct => Task.Run(vm.Inspector.Adapters, ct), force, token);
            await endpointCache.ReadAsync(ct => Task.Run(() => new ConnectionSnapshot(vm.Inspector.Connections()), ct), force, token);
        }
        if (destination == NavigationDestination.HardwareOverview)
        {
            await vm.Storage.ReadAsync(force, token);
            var devices = await vm.Devices.SnapshotAsync(force, token);
            token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation)) lastDevices = devices;
        }
        if (destination is NavigationDestination.ActivityOverview or NavigationDestination.CoreOverview)
        {
            var processes = await Task.Run(vm.Inspector.Processes, token);
            token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation)) vm.SetProcesses(processes, false);
        }
        token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation) && routes.Selected == destination) UpdateHomepageSummary();
    }
    private async Task<InspectionTable> ReadNetwork(string view, bool force, CancellationToken token)
    {
        if (view is "Connections" or "Timeline")
        {
            var endpoints = await endpointCache.ReadAsync(ct => Task.Run(() => new ConnectionSnapshot(vm.Inspector.Connections()), ct), force, token);
            return view == "Connections" ? PresentationTables.Connections(endpoints.Rows) : ConnectionTimelineTable.Create(vm.Inspector.ConnectionEvents());
        }
        if (view == "DNS") return await Task.Run(vm.Inspector.DnsConfiguration, token);
        var adapters = await adapterCache.ReadAsync(ct => Task.Run(vm.Inspector.Adapters, ct), force, token);
        return view == "Traffic" ? NetworkTables.Traffic(adapters) : adapters;
    }
    private InspectionTable ReadSecurity()
    {
        var privilege = vm.Privileges.Current();
        var rows = new List<string[]> { new[] { "Account", privilege.User }, new[] { "Elevated", privilege.Elevated.ToString() }, new[] { "Authorization", privilege.Mechanism }, new[] { "Firewall status", "Unsupported: no firewall provider is installed" } };
        var host = vm.Capabilities.Platform;
        rows.AddRange(new[] { new[] { "Host operating system", host.Description }, new[] { "Kernel / build", host.Version }, new[] { "Hostname", host.Hostname }, new[] { "OS / process architecture", $"{host.OSArchitecture} / {host.ProcessArchitecture}" } });
        rows.AddRange(vm.Capabilities.Capabilities.Select(c => new[] { c.Id, $"{c.Status} · {c.Source} · {c.Reason}" }));
        return new("Security and permissions", ["Property", "Value"], rows, "Read-only current-token and provider information. This page does not change security settings or silently elevate MachinScope.");
    }
}
