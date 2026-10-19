using Core.Application.Services;
using Core.Desktop.Controls;
using Core.Domain.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Core.Desktop;

public sealed partial class MainWindow
{
    private async Task InspectConnectionOwner(ConnectionRow connection)
    {
        // Reuse cached owner metadata only for the exact creation identity.
        var owner = ProcessRelationships.Owner(connection, vm.Processes);
        await InspectProcess(owner);
    }

    private async Task NetworkProperties(InspectionRow selected)
    {
        var frozenAdapter = selected.Subject as InspectionRow;
        if (selected.Subject is not ConnectionRow && frozenAdapter == null && selected.Subject is not string) return;
        InspectionRow? drillDown = null;
        ContentDialog? currentDialog = null;
        await Work(async token => {
            var width = Math.Clamp(Root.ActualWidth - 100, 320, 1120);
            var tabs = new TabView { IsAddTabButtonVisible = false, Height = Math.Clamp(Root.ActualHeight - 310, 250, 620), Width = width - 48, TabWidthMode = TabViewWidthMode.SizeToContent };
            async Task Add(InspectionTable table) {
                var control = new InspectionTableControl();
                void Inspect(InspectionRow row) { if (row.Subject is ConnectionRow) { drillDown = row; currentDialog?.Hide(); } }
                control.RowInvoked += Inspect;
                control.ObjectMenu = (_, row, _) => {
                    var menu = new MenuFlyout();
                    if (row?.Subject is ConnectionRow) { var item = new MenuFlyoutItem { Text = "Properties" }; item.Click += (_, _) => Inspect(row); menu.Items.Add(item); }
                    return menu;
                };
                await control.SetTableAsync(table);
                tabs.TabItems.Add(new TabViewItem { Header = table.Title, IsClosable = false, Content = control });
            }
            string title;
            if (selected.Subject is ConnectionRow connection)
            {
                title = $"{connection.Protocol} · {connection.ProcessName} · PID {connection.ProcessId}";
                await Add(NetworkInspection.Properties(connection));
                var owner = ProcessRelationships.Owner(connection, vm.Processes);
                var details = await vm.Inspector.DetailsAsync(owner, token);
                foreach (var table in new[] { details.General with { Title = "Owner" }, details.Threads, details.Modules, details.Services }) await Add(table);
            }
            else
            {
                var source = adapterCache.Last ?? throw new InvalidOperationException("Adapter snapshot unavailable; refresh first.");
                var adapter = frozenAdapter ?? TableRows.From(source).FirstOrDefault(r => r.Key == selected.Key) ?? throw new InvalidOperationException("Adapter no longer present in the snapshot. Refresh first.");
                title = adapter.Cells[0];
                var fields = source.Columns.Select((name, i) => new[] { name, adapter.Cells.ElementAtOrDefault(i) ?? "Unavailable" }).ToList();
                fields.Add(["Interface ID", adapter.Key]);
                fields.Add(["Rate availability", adapter.Cells.ElementAtOrDefault(2) != "Up" ? "Interface is down or its provider is unavailable. Windows may expose virtual/filter interfaces without readable counters." : "Rates require two samples. A counter reset makes the next rate unavailable."]);
                fields.Add(["Attribution", "Interface counters cover all owners. Related connections match concrete local addresses; wildcard listeners and per-process bandwidth cannot be attributed to this adapter."]);
                await Add(new("Interface", ["Property", "Value"], fields));
                var endpoints = await endpointCache.ReadAsync(ct => Task.Run(() => new ConnectionSnapshot(vm.Inspector.Connections()), ct), false, token);
                await Add(PresentationTables.Connections(NetworkInspection.ForAdapter(adapter, endpoints.Rows)));
            }
            token.ThrowIfCancellationRequested();
            var dialog = new ContentDialog { Title = title, Content = tabs, CloseButtonText = "Close", XamlRoot = Root.XamlRoot };
            currentDialog = dialog; dialog.Resources["ContentDialogMaxWidth"] = width; await ShowInspectionDialog(dialog, tabs, selected.Subject is ConnectionRow ? InspectionScope.Connection : InspectionScope.Adapter);
        });
        if (drillDown != null && !closing) await NetworkProperties(drillDown);
    }
}
