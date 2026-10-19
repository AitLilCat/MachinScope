using Core.Application.Services;

using Core.Domain.Models;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Automation;

using Microsoft.UI.Xaml.Controls;

using Microsoft.UI.Xaml.Controls.Primitives;

namespace Core.Desktop;



public sealed partial class MainWindow

{

    private NavigationCoordinator routes = null!;

    private readonly Dictionary<NavigationDestination, NavigationViewItem> navigationItems = new();

    private readonly Dictionary<NavigationCategory, NavigationViewItem> categoryItems = new();

    private readonly Dictionary<NavigationDestination, (string Search, string Mode)> inspectorQueries = new();

    private readonly Dictionary<NavigationDestination, string> networkStates = new();

    private readonly DispatcherTimer navigationSave = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private NavigationDestination? currentDestination;

    private bool restoringNavigation;




    private void InitializeNavigation()

    {

        routes = new(vm.Capabilities.Platform.Kind, vm.Capabilities.Capabilities, vm.Preferences.Navigation);

        Navigation.IsPaneOpen = routes.PaneOpen;

        Navigation.MenuItems.Add(CreateNavigationItem(NavigationRegistry.Get(NavigationDestination.CoreOverview), Symbol.Home));

        foreach (var group in NavigationRegistry.Groups)

        {

            var item = CreateNavigationItem(NavigationRegistry.Get(group.Homepage), group.Category switch { NavigationCategory.Activity => Symbol.ShowResults, NavigationCategory.Hardware => Symbol.Save, NavigationCategory.Network => Symbol.World, _ => Symbol.AllApps });

            item.Content = group.Name; AutomationProperties.SetName(item, group.Name + " overview");

            foreach (var route in NavigationRegistry.Routes.Where(r => r.Category == group.Category && r.Visible && (r.Platform == null || r.Platform == vm.Capabilities.Platform.Kind) && r.Destination != group.Homepage))

            {

                var child = CreateNavigationItem(route);

                item.MenuItems.Add(child);

            }

            categoryItems.Add(group.Category, item); navigationItems[group.Homepage] = item;

            item.IsExpanded = routes.IsExpanded(group.Category);

            item.RegisterPropertyChangedCallback(NavigationViewItem.IsExpandedProperty, (_, _) => { if (!initialized) return; routes.SetExpanded(group.Category, item.IsExpanded); QueueNavigationSave(); });

            Navigation.MenuItems.Add(item);

        }

        Navigation.FooterMenuItems.Add(new NavigationViewItemSeparator());

        Navigation.FooterMenuItems.Add(CreateNavigationItem(NavigationRegistry.Get(NavigationDestination.Preferences), Symbol.Setting));

        Navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => { if (!initialized) return; routes.PaneOpen = Navigation.IsPaneOpen; QueueNavigationSave(); });

        navigationSave.Tick += (_, _) => { navigationSave.Stop(); PersistNavigation(); };

    }

    private NavigationViewItem CreateNavigationItem(NavigationRoute route, Symbol? icon = null)

    {

        var decision = NavigationRegistry.Availability(route, vm.Capabilities.Platform.Kind, vm.Capabilities.Capabilities);

        var item = new NavigationViewItem { Content = route.Name + (decision.Allowed ? "" : " · unavailable"), Tag = route.Destination, IsEnabled = decision.Allowed };

        if (icon is { } symbol) item.Icon = new SymbolIcon(symbol);

        AutomationProperties.SetName(item, route.Name + (decision.Allowed ? "" : ": " + decision.Reason));

        AutomationProperties.SetAutomationId(item, route.Id);

        ToolTipService.SetToolTip(item, decision.Allowed ? route.Name : decision.Reason);

        navigationItems[route.Destination] = item;

        return item;

    }

    private void RestoreNavigation()

    {

        restoringNavigation = true;

        try { Navigation.IsPaneOpen = routes.PaneOpen; Navigation.SelectedItem = navigationItems[routes.Selected]; }

        finally { restoringNavigation = false; }

        SyncExpansion();

    }

    private void NavigateTo(NavigationDestination destination)

    {

        if (closing || !routes.Select(destination)) return;

        SyncExpansion();

        Navigation.SelectedItem = navigationItems[destination];

        QueueNavigationSave();

    }

    private void OnNavigationInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)

    {

        if (args.InvokedItemContainer is NavigationViewItem { Tag: NavigationDestination destination } && NavigationRegistry.Get(destination) is { Homepage: true, Category: { } category })

        {

            if (NavigationRegistry.Groups.All(g => g.Homepage != destination)) return;
            var expandedBeforeLabel = routes.IsExpanded(category);
            DispatcherQueue.TryEnqueue(() => {
                if (closing || routes.Selected != destination || !ReferenceEquals(Navigation.SelectedItem, navigationItems[destination])) return;
                routes.SetExpanded(category, expandedBeforeLabel); SyncExpansion(); QueueNavigationSave();
            });
        }

    }

    private void SyncExpansion() { foreach (var (category, item) in categoryItems) item.IsExpanded = routes.IsExpanded(category); }

    private void QueueNavigationSave() { if (!initialized || closing) return; navigationSave.Stop(); navigationSave.Start(); }

    private void PersistNavigation()

    {

        try { if (routes != null) vm.SavePreferences(vm.Preferences with { Navigation = routes.Snapshot() }); }

        catch (Exception e) { vm.Error(e); }

    }

    private void SetChipQuery(string query)

    {

        DataTable.ModeQuery = query;

        foreach (var button in FilterBar.Children.OfType<ToggleButton>()) button.IsChecked = button.Tag?.ToString() == query;

    }

    private void AddShortcut(StackPanel stack, NavigationRoute route, string? label = null)
    {
        var decision = NavigationRegistry.Availability(route, vm.Capabilities.Platform.Kind, vm.Capabilities.Capabilities);
        var button = new Button { Content = label ?? route.Name + (decision.Allowed ? "" : " · unavailable"), IsEnabled = decision.Allowed, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, route.Name); ToolTipService.SetToolTip(button, decision.Reason);
        button.Click += (_, _) => NavigateTo(route.Destination); stack.Children.Add(button);
    }
    private void UpdateNavigationHeader()
    {
        HistoryBack.IsEnabled = routes.CanBack; HistoryForward.IsEnabled = routes.CanForward;
        var route = NavigationRegistry.Get(routes.Selected);
        PageHeading.Text = route.Category is { } category && NavigationRegistry.Groups.Single(g => g.Category == category) is { } group && group.Homepage != route.Destination ? group.Name + " / " + route.Name : route.Name == "Overview" ? NavigationRegistry.Groups.Single(g => g.Category == route.Category).Name : route.Name;
    }
    private void OnHistoryBack(object sender, RoutedEventArgs args) { if (!closing && routes.Back()) { SyncExpansion(); Navigation.SelectedItem = navigationItems[routes.Selected]; UpdateNavigationHeader(); } }
    private void OnHistoryForward(object sender, RoutedEventArgs args) { if (!closing && routes.Forward()) { SyncExpansion(); Navigation.SelectedItem = navigationItems[routes.Selected]; UpdateNavigationHeader(); } }
}
