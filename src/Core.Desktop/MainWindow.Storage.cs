using Core.Application.Services;

using Core.Desktop.Controls;

using Core.Domain.Models;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

namespace Core.Desktop;



public sealed partial class MainWindow

{

    private DiskInventory? diskInventory;

    private async Task<InspectionTable> ReadStorage(bool force, CancellationToken token)
    {
        var snapshot = await vm.Storage.ReadAsync(force, token); token.ThrowIfCancellationRequested();
        diskInventory = snapshot.Inventory;
        return snapshot.Table with { Notice = "One storage snapshot. Filters retain physical disks, partitions, volumes, mappings, provider rows and counters. Double-click for properties; provider status is not decoded SMART health." };
    }
    private async Task<InspectionTable> ReadDiskHealth(bool force, CancellationToken token)
    {
        var snapshot = await vm.Storage.ReadAsync(force, token); token.ThrowIfCancellationRequested(); diskInventory = snapshot.Inventory;
        var rows = snapshot.Inventory.Disks.Select(d => { string[] cells = [d.Target.Number.ToString(), d.Target.Model, d.ProviderStatus, "Open Properties → Health", "Unavailable unless reported by the reliability provider"];
            return new InspectionRow(d.Target.Identity, cells, cells, new Dictionary<string, object?> { ["name"] = d.Target.Model }, d.Target); }).ToArray();
        return new("Disk health", ["Disk", "Model", "Provider status", "Reliability diagnostics", "SMART / temperature"], rows.Select(r => r.Cells).ToArray(), "Double-click a disk to inspect existing Health and Events tabs. Provider status is not a decoded SMART/NVMe report; storage events are labeled shared/unattributed.", rows);
    }
    private async Task DiskProperties(DiskTarget target)

    {

        DiskDetails? details = null;

        await Work(async token => details = await vm.Disks.DetailsAsync(target, token));

        if (details == null) return;

        {

            var width = Math.Clamp(Root.ActualWidth - 100, 320, 1120);

            var tabs = new TabView { IsAddTabButtonVisible = false, TabWidthMode = TabViewWidthMode.SizeToContent, Width = width - 48, Height = Math.Clamp(Root.ActualHeight - 300, 280, 650) };

            InspectionRow? related = null;
            ContentDialog? currentDialog = null;
            foreach (var table in details.Tabs)

            {

                var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });

                panel.Children.Add(new TextBlock { Text = table.Notice ?? "", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8), FontSize = 12 });

                var control = new InspectionTableControl(); Grid.SetRow(control, 1); panel.Children.Add(control); await control.SetTableAsync(table);
                void Inspect(InspectionRow row) { if (row.Subject is VolumeTarget or PartitionTarget) { related = row; currentDialog?.Hide(); } }
                control.RowInvoked += Inspect;
                control.ObjectMenu = (_, row, _) => { var menu = new MenuFlyout(); if (row?.Subject is VolumeTarget or PartitionTarget) { var item = new MenuFlyoutItem { Text = "Properties" }; item.Click += (_, _) => Inspect(row); menu.Items.Add(item); } return menu; };

                tabs.TabItems.Add(new TabViewItem { Header = table.Title, IsClosable = false, Content = panel });

            }

            var dialog = new ContentDialog { Title = $"Disk {target.Number} · {target.Model}", Content = tabs, CloseButtonText = "Close", XamlRoot = Root.XamlRoot }; currentDialog = dialog; dialog.Resources["ContentDialogMaxWidth"] = width; await ShowInspectionDialog(dialog, tabs, InspectionScope.Disk);
            if (related?.Subject is VolumeTarget volume) await VolumeProperties(volume);
            else if (related?.Subject is PartitionTarget) await InspectRowValues("Partition snapshot", details.Tabs.First(t => t.Title == "Layout").Columns, related);

        }

    }

    private Task VolumeProperties(VolumeTarget volume) => InspectValues("Volume properties · captured snapshot", new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = $"Path: {volume.Path}\nDevice: {volume.DeviceId}\nLabel: {volume.Label}\nFilesystem: {volume.FileSystem}\nCapacity: {(volume.Capacity.HasValue ? Calculations.Bytes(volume.Capacity.Value) : "Unavailable")}\nFree: {(volume.Free.HasValue ? Calculations.Bytes(volume.Free.Value) : "Unavailable")}\nSystem: {volume.System}\nBoot: {volume.Boot}\nPagefile: {volume.PageFile}\nDisk backing (cached inventory, not revalidated): {string.Join(", ", diskInventory?.Disks.Where(d => d.Partitions.Any(p => p.Volumes.Any(v => v.DeviceId == volume.DeviceId))).Select(d => d.Target.DevicePath) ?? [])}" });

    private async Task OpenVolumeInFiles(VolumeTarget volume)

    {

        if (!Directory.Exists(volume.Path)) throw new DirectoryNotFoundException(volume.Path);

        directory = volume.Path;

        var alreadyFiles = currentDestination == NavigationDestination.Files;

        NavigateTo(NavigationDestination.Files);

        if (alreadyFiles) await Refresh();

    }

}

