using Core.Application.Services;

using Core.Desktop.Controls;

using Core.Domain.Models;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

namespace Core.Desktop;



public sealed partial class MainWindow

{

    private string deviceView = "USB";

    private ComboBox? deviceViews;

    private bool navigatingDevices;

    private long measuredEnumeration;

    private void BuildDeviceToolbar()

    {

        var views = new ComboBox { ItemsSource = new[] { "USB", "All devices", "Problems", "Events" }, SelectedItem = deviceView }; deviceViews = views;

        views.SelectionChanged += async (_, _) => {

            if (navigatingDevices) return;

            deviceView = views.SelectedItem.ToString()!; DataTable.PreserveSourceOrder = deviceView == "Events";

            DataTable.Search = ""; await Refresh();

        };

        Toolbar.Children.Add(views);

        AddButton("Properties", () => DataTable.SelectedRows.FirstOrDefault()?.Subject is DeviceTarget target ? DeviceProperties(target) : Dialog("Devices", "Select a device to inspect."));

    }

    private async Task<InspectionTable> ReadDevices(bool force, CancellationToken token)

    {

        var view = deviceView;

        var snapshot = await vm.Devices.SnapshotAsync(force, token);
        token.ThrowIfCancellationRequested(); lastDevices = snapshot;

        if (vm.Measurements.Enabled && snapshot.EnumerationCount != measuredEnumeration)

        {

            measuredEnumeration = snapshot.EnumerationCount;

            vm.Measurements.Record(DiagnosticMetric.DeviceEnumeration, snapshot.EnumerationTime, RefreshState.Applied);

        }

        return view == "Events" ? DeviceTables.Events(snapshot) : DeviceTables.Inventory(snapshot, view);

    }

    private void AddDeviceContext(MenuFlyout menu, IReadOnlyList<InspectionRow> selected, InspectionRow? row)

    {

        if (row?.Subject is not DeviceTarget target) return;

        void Item(string text, Func<Task> action) {

            var item = new MenuFlyoutItem { Text = text };

            item.Click += async (_, _) => { try { await action(); } catch (Exception e) { vm.Error(e); } };

            menu.Items.Add(item);

        }


        Item("Inspect parent", () => RelatedDevice(target, true));

        Item("View child devices", () => RelatedDevice(target, false));

        Item("Refresh device inventory", Refresh);

        Item("Export selected devices…", ExportSelected);

        Item("Properties", () => DeviceProperties(target));

    }

    private async Task RelatedDevice(DeviceTarget target, bool parent)

    {

        var snapshot = await vm.Devices.SnapshotAsync(true, lifetime.Token);

        var current = snapshot.Devices.SingleOrDefault(d => d.Target?.Identity == target.Identity);

        if (current == null) throw new IOException("Device disappeared or changed. Refresh and select again.");

        if (parent) {

            var device = snapshot.Devices.FirstOrDefault(d => d.InstanceId.Equals(current.ParentId, StringComparison.OrdinalIgnoreCase));

            if (device == null) { await Dialog("Parent device", "Parent is unavailable or outside the present inventory."); return; }

            await DeviceProperties(device.Target!);

        } else {

            deviceView = "All devices";

            navigatingDevices = true;

            try { if (deviceViews != null) deviceViews.SelectedItem = deviceView; } finally { navigatingDevices = false; }

            vm.SetTable(DeviceTables.Inventory(snapshot, deviceView));

            DataTable.Search = "parent:" + current.InstanceId;

        }

    }

    private async Task DeviceProperties(DeviceTarget target)

    {

        DeviceDetails? details = null;

        await Work(async token => details = await vm.Devices.DetailsAsync(target, token));

        if (details == null) return;

        var width = Math.Clamp(Root.ActualWidth - 100, 320, 1120);

        var tabs = new TabView { IsAddTabButtonVisible = false, TabWidthMode = TabViewWidthMode.SizeToContent,

            Width = width - 48, Height = Math.Clamp(Root.ActualHeight - 300, 280, 650) };

        var dialog = new ContentDialog { Title = details.Name, Content = tabs, CloseButtonText = "Close", XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };

        dialog.Resources["ContentDialogMaxWidth"] = width;

        DeviceTarget? nextDevice = null; VolumeTarget? nextVolume = null; DiskTarget? nextDisk = null;

        foreach (var table in details.Tabs) {

            var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });

            panel.Children.Add(new TextBlock { Text = table.Notice ?? "", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8), FontSize = 12 });

            var control = new InspectionTableControl(); Grid.SetRow(control, 1); panel.Children.Add(control); await control.SetTableAsync(table);

            control.RowInvoked += row => {

                if (row.Subject is DeviceTarget device) { nextDevice = device; dialog.Hide(); }

                else if (row.Subject is VolumeTarget volume) { nextVolume = volume; dialog.Hide(); }

                else if (row.Subject is DiskTarget disk) { nextDisk = disk; dialog.Hide(); }

            };

            tabs.TabItems.Add(new TabViewItem { Header = table.Title, IsClosable = false, Content = panel });

        }

        await ShowInspectionDialog(dialog, tabs, InspectionScope.Device);

        if (nextDevice != null) await DeviceProperties(nextDevice);

        else if (nextVolume != null) await OpenVolumeInFiles(nextVolume);

        else if (nextDisk != null) await DiskProperties(nextDisk);

    }

}

