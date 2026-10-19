using Core.Application.Services;
using Core.Desktop.Controls;
using Core.Domain.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Core.Desktop;
public sealed partial class MainWindow
{
    private async Task Launch()
    {
        var executable = new TextBox { Header = "Absolute executable path" }; var cwd = new TextBox { Header = "Working directory", Text = directory }; var arguments = new TextBox { Header = "Arguments · one per line (no shell parsing)", AcceptsReturn = true, Height = 100 }; var stack = new StackPanel { Spacing = 8 }; stack.Children.Add(executable); stack.Children.Add(cwd); stack.Children.Add(arguments);
        if (await ShowDialog(new ContentDialog { Title = "Record launch context", Content = stack, PrimaryButtonText = "Review", CloseButtonText = "Cancel", XamlRoot = Root.XamlRoot }) != ContentDialogResult.Primary) return;
        var spec = new LaunchSpec(executable.Text, arguments.Text.Length == 0 ? [] : arguments.Text.Replace("\r", "").Split('\n'), cwd.Text);
        if (!await Confirm("Launch executable", $"Executable: {spec.Executable}\nDirectory: {spec.WorkingDirectory}\nArguments: {string.Join(" | ", spec.Arguments)}")) return;
        await Work(async token => { var started = await Task.Run(() => vm.Inspector.LaunchManaged(spec), token); vm.Status = $"Launched PID {started.Id}"; }); await Refresh();
    }
    private async Task ProcessControl()
    {
        var frozen = DataTable.SelectedRows.Select(r => r.Subject).OfType<ProcessSample>().ToArray(); if (frozen.Length == 0) { vm.Status = "Select one or more processes."; return; }
        var actions = new ComboBox { ItemsSource = new[] { "End process", "End observed tree", "Restart", "Priority", "Affinity" }, SelectedIndex = 0 }; var priority = new ComboBox { ItemsSource = new[] { "Idle", "BelowNormal", "Normal", "AboveNormal", "High" }, SelectedIndex = 2 }; var mask = new TextBox { Header = "Affinity mask (hex, e.g. 3 = CPU 0 and 1)", Text = "1" }; var stack = new StackPanel { Spacing = 8 }; stack.Children.Add(new TextBlock { Text = $"{frozen.Length} selected processes · targets remain frozen during review." }); stack.Children.Add(actions); stack.Children.Add(priority); stack.Children.Add(mask);
        if (await ShowDialog(new ContentDialog { Title = "Process control", Content = stack, PrimaryButtonText = "Review", CloseButtonText = "Cancel", XamlRoot = Root.XamlRoot }) != ContentDialogResult.Primary) return;
        var action = actions.SelectedIndex; var selectedPriority = priority.SelectedItem.ToString()!; var affinity = action == 4 ? Convert.ToUInt64(mask.Text, 16) : 0;
        if (action == 1) { var snapshot = await Task.Run(vm.Inspector.Processes); frozen = frozen.SelectMany(p => ProcessTree.ObservedDescendants(p, snapshot)).DistinctBy(p => p.Identity).ToArray(); }
        if (action == 2 && frozen.Any(p => !vm.Inspector.CanRestart(p).Available)) { await Dialog("Restart unavailable", new TextBlock { Text = vm.Inspector.CanRestart(frozen.First(p => !vm.Inspector.CanRestart(p).Available)).Reason, TextWrapping = TextWrapping.Wrap }); return; }
        var preview = string.Join("\n", frozen.Select(p => $"{p.Name} · PID {p.Id} · started {p.Started:O}"));
        if (!await Confirm("Confirm " + actions.SelectedItem, $"{preview}\nPriority: {selectedPriority} · affinity: {mask.Text}\nEach identity is revalidated during the operation. Tree scope contains observed descendants only. Unreadable creation times are excluded. Ending or restarting may lose unsaved data.")) return;
        IReadOnlyList<ActionItemResult> results = [];
        var typed = new ProcessAction(vm.Inspector, action switch { 0 or 1 => ProcessOperation.End, 2 => ProcessOperation.Restart, 3 => ProcessOperation.Priority, _ => ProcessOperation.Affinity }, selectedPriority, affinity);
        await Work(async token => results = await vm.Actions.ExecuteAsync(typed, frozen, frozen.Select(p => p.Identity).ToHashSet(), new Progress<ActionProgress>(p => vm.Status = $"{p.Completed}/{p.Total} · {p.CurrentIdentity}"), token));
        await Dialog("Individual results", new TextBox { Text = string.Join("\n", results.Select(r => $"{r.Identity}: {r.Outcome} · {r.Message}")), AcceptsReturn = true, IsReadOnly = true, MaxHeight = 450 }); await Refresh();
    }
    private Task Details() => InspectProcess(SelectedProcess);
    private async Task InspectProcess(ProcessSample? process)
    { if (process == null) { vm.Status = "Select a process."; return; }
        Func<Task>? followRelationship = null;
        ContentDialog? currentDialog = null;
        await Work(async token => {
            var details = await vm.Inspector.DetailsAsync(process, token); token.ThrowIfCancellationRequested();
            var detailWidth = Math.Clamp(Root.ActualWidth - 100, 320, 1120); var stack = new StackPanel { Spacing = 8, Width = detailWidth - 48 }; var tabs = new TabView { IsAddTabButtonVisible = false, Height = Math.Clamp(Root.ActualHeight - 400, 250, 620), HorizontalAlignment = HorizontalAlignment.Stretch, TabWidthMode = TabViewWidthMode.SizeToContent };
            foreach (var table in new[] { details.General, details.Threads, details.Modules, details.Services, details.Connections }) {
                var control = new InspectionTableControl(); await control.SetTableAsync(table);
                bool Related(InspectionRow row) => row.Subject is ConnectionRow || table.Title is "Modules" or "Services";
                void Inspect(InspectionRow row) {
                    if (row.Subject is ConnectionRow) followRelationship = () => NetworkProperties(row);
                    else if (table.Title == "Modules" && row.Raw.ElementAtOrDefault(1) is { } modulePath && File.Exists(modulePath)) followRelationship = () => FileProperties(modulePath);
                    else if (table.Title is "Modules" or "Services") followRelationship = () => InspectRowValues(table.Title + " · selected process snapshot", table.Columns, row);
                    else return;
                    currentDialog?.Hide();
                }
                control.RowInvoked += Inspect;
                control.ObjectMenu = (_, row, _) => {
                    var menu = new MenuFlyout();
                    if (row != null && Related(row)) { var item = new MenuFlyoutItem { Text = "Properties" }; item.Click += (_, _) => Inspect(row); menu.Items.Add(item); }
                    return menu;
                };
                tabs.TabItems.Add(new TabViewItem { Header = table.Title, IsClosable = false, Content = control });
            }
            tabs.TabItems.Add(new TabViewItem { Header = "Performance", IsClosable = false, Content = new TextBlock { Text = $"Snapshot CPU: {process.CpuPercent:0.00}%\nCPU time: {process.CpuTime}\nWorking set: {process.WorkingSet} B\nPrivate memory: {process.PrivateBytes} B\nRead/write: {process.ReadRate} / {process.WriteRate} B/s", TextWrapping = TextWrapping.Wrap } });
            tabs.TabItems.Add(new TabViewItem { Header = "Handles", IsClosable = false, Content = new TextBlock { Text = $"Handle count: {process.Handles?.ToString() ?? "Unavailable"}. Detailed handle enumeration is unavailable through the supported provider.", TextWrapping = TextWrapping.Wrap } }); stack.Children.Add(tabs);
            var parent = ProcessRelationships.Parent(process, vm.Processes);
            if (parent != null) { var inspectParent = new Button { Content = $"Inspect observed parent · {parent.Name} · PID {parent.Id}" }; inspectParent.Click += (_, _) => { followRelationship = () => InspectProcess(parent); currentDialog?.Hide(); }; stack.Children.Add(inspectParent); }
            var path = details.General.Rows.FirstOrDefault(r => r[0] == "Executable")?.ElementAtOrDefault(1);
            if (path != null && File.Exists(path)) { var folder = new Button { Content = "Open executable folder" }; folder.Click += (_, _) => vm.Desktop.ShowInFolder(path); stack.Children.Add(folder); var properties = new Button { Content = "Windows file properties / signature" }; properties.Click += (_, _) => vm.Desktop.Properties(path); stack.Children.Add(properties); var hash = new Button { Content = "SHA-256" }; hash.Click += async (_, _) => { try { await using var file = File.OpenRead(path); var digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, token)); InspectionTableControl.PutClipboard(digest); vm.Status = "SHA-256 copied: " + digest; } catch (Exception e) { vm.Error(e); } }; stack.Children.Add(hash); }
            var dialog = new ContentDialog { Title = $"{process.Name} · PID {process.Id}", Content = stack, CloseButtonText = "Close", XamlRoot = Root.XamlRoot }; currentDialog = dialog; dialog.Resources["ContentDialogMaxWidth"] = detailWidth; await ShowInspectionDialog(dialog, tabs, InspectionScope.Process);
        });
        if (followRelationship != null && !closing) await followRelationship();
    }
}
