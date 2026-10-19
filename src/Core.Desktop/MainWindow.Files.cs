using Core.Application.Services;
using Core.Domain.Models;

using Core.Desktop.Controls;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

namespace Core.Desktop;

public sealed partial class MainWindow

{

    private readonly Core.Application.Services.BrowseHistory browseHistory = new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private Button? backButton, forwardButton, fileCancel;

    private void BuildFilesToolbar()

    {

        browseHistory.Navigate(directory);

        Button NavigationButton(string label, Func<Task> action) { var button = new Button { Content = label, FontSize = 12, Padding = new Thickness(8, 5, 8, 5) }; button.Click += async (_, _) => { try { await action(); } catch (Exception e) { vm.Error(e); } }; Toolbar.Children.Add(button); return button; }

        backButton = NavigationButton("Back", async () => { directory = browseHistory.Back(); UpdateBrowseButtons(); await Refresh(); });

        forwardButton = NavigationButton("Forward", async () => { directory = browseHistory.Forward(); UpdateBrowseButtons(); await Refresh(); });

        NavigationButton("Up", () => NavigateDirectory(Directory.GetParent(directory)?.FullName ?? directory));

        pathBox = new TextBox { Text = directory, Width = 360, PlaceholderText = "Current path · Enter to navigate" }; pathBox.KeyDown += async (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) { e.Handled = true; try { await NavigateDirectory(pathBox.Text); } catch (Exception ex) { vm.Error(ex); } } }; Toolbar.Children.Add(pathBox);

        AddButton("Refresh", () => NavigateDirectory(pathBox.Text));

        var filter = new TextBox { PlaceholderText = "Search · ext:log size>100MiB", Width = 230, Text = currentDestination is { } destination ? inspectorQueries.GetValueOrDefault(destination).Search ?? "" : "" }; activeFilter = filter; filter.TextChanged += (_, _) => DataTable.Search = filter.Text; Toolbar.Children.Add(filter);

        var actions = new MenuFlyout();

        void Action(string title, Func<Task> run) { var item = new MenuFlyoutItem { Text = title }; item.Click += async (_, _) => { try { await run(); } catch (Exception e) { vm.Error(e); } }; actions.Items.Add(item); }

        Action("Scan current folder", Scan); Action("Find duplicates", Duplicates); Action("Export…", Export); Action("Columns…", () => DataTable.ConfigureColumnsAsync(Root.XamlRoot)); Action("File operations…", FileAction);

        Toolbar.Children.Add(new Button { Content = "Actions", Flyout = actions, FontSize = 12, Padding = new Thickness(8, 5, 8, 5) });

        fileCancel = NavigationButton("Cancel", () => { operation?.Cancel(); return Task.CompletedTask; }); fileCancel.Visibility = Visibility.Collapsed;

        UpdateBrowseButtons();

    }

    private void UpdateBrowseButtons()

    {

        if (backButton != null) backButton.IsEnabled = browseHistory.CanBack;

        if (forwardButton != null) forwardButton.IsEnabled = browseHistory.CanForward;

        if (pathBox != null) pathBox.Text = directory;

    }

    private async Task NavigateDirectory(string path)

    {

        var full = Path.GetFullPath(path); if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);

        directory = full; browseHistory.Navigate(full); UpdateBrowseButtons(); await Refresh();

    }

    private void AddFileContext(MenuFlyout menu, IReadOnlyList<InspectionRow> selected, InspectionRow? row)

    {

        void Item(string name, Func<Task> run) { var item = new MenuFlyoutItem { Text = name }; item.Click += async (_, _) => { try { await run(); } catch (Exception e) { vm.Error(e); } }; menu.Items.Add(item); }

        if (vm.Table.Title != "Files" || row == null) { Item("Export results…", Export); return; }

        var path = row.Raw[5]; var folder = row.Raw[1] == "Directory";

        Item("Open", async () => { if (folder) await NavigateDirectory(path); else vm.Desktop.Open(path); });

        if (folder)

        {

            Item("Scan folder / calculate size", async () => { await NavigateDirectory(path); await Scan(); });

            Item("Find duplicates", async () => { await NavigateDirectory(path); await Duplicates(); });

        }

        else { Item("Copy / move / rename / recycle…", FileAction); Item("Inspect locking processes", async () => await Work(async t => { var users = await Task.Run(() => vm.Desktop.FileUsers(path), t); await Dialog("File users", new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = string.Join("\n", users.Rows.Select(r => string.Join(" | ", r))) + "\n" + users.Notice }); })); }



        Item("Export selection…", ExportSelected);

        Item("Windows properties", () => { vm.Desktop.Properties(path); return Task.CompletedTask; });

        Item("Properties", () => FileProperties(path));

    }

    private Task FileProperties(string path) => Work(async token =>

    {

        using var hashCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        var hashing = false;

        Task<string>? hashTask = null;

        var details = await vm.FileProperties.DetailsAsync(path, token);

        var width = Math.Clamp(Root.ActualWidth - 100, 320, 1120);

        var panel = new StackPanel { Spacing = 8, Width = width - 48 };

        var tabs = new TabView { IsAddTabButtonVisible = false, TabWidthMode = TabViewWidthMode.SizeToContent, Height = Math.Clamp(Root.ActualHeight - 420, 220, 480) };

        foreach (var table in details.Tabs)

        {

            var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock { Text = table.Notice ?? "", TextWrapping = TextWrapping.Wrap, FontSize = 12 });

            var control = new InspectionTableControl(); Grid.SetRow(control, 1); grid.Children.Add(control); await control.SetTableAsync(table);

            tabs.TabItems.Add(new TabViewItem { Header = table.Title, IsClosable = false, Content = grid });

        }

        panel.Children.Add(tabs);

        var result = new TextBox { MinHeight = 48, MaxHeight = 90, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "Checksums are calculated only when requested" };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        foreach (var algorithm in new[] { "SHA-256", "SHA-512" })

        {

            var button = new Button { Content = algorithm, IsEnabled = !details.Target.Directory };

            button.Click += async (_, _) => { if (hashing || closing) return; hashing = true; button.IsEnabled = false; try { result.Text = "Reading file…"; hashTask = vm.FileProperties.HashAsync(details.Target, algorithm, hashCancellation.Token); var digest = await hashTask; if (!hashCancellation.IsCancellationRequested) result.Text = digest; } catch (Exception e) { if (!closing) result.Text = e.Message; } finally { hashing = false; button.IsEnabled = !details.Target.Directory; } }; buttons.Children.Add(button);

        }

        panel.Children.Add(buttons); panel.Children.Add(result);

        var dialog = new ContentDialog { Title = Path.GetFileName(path), Content = panel, CloseButtonText = "Close", XamlRoot = Root.XamlRoot }; dialog.Resources["ContentDialogMaxWidth"] = width;

        await ShowInspectionDialog(dialog, tabs, InspectionScope.File); hashCancellation.Cancel();

        if (hashTask != null) { try { await hashTask; } catch (OperationCanceledException) { } catch (Exception e) { if (!closing) vm.Error(e); } }

    });

    private Task Scan() => Work(async token =>

    {

        var report = await vm.Files.ScanAsync(pathBox?.Text ?? directory, vm.Preferences.IncludeHiddenFiles, OperationProgress(), token); token.ThrowIfCancellationRequested();

        var rows = new List<string[]> { new[] { "Total logical bytes", report.Bytes.ToString(), report.Root }, new[] { "Files / folders", $"{report.Files} / {report.Folders}", "" }, new[] { "Depth / reparse skipped", $"{report.MaxDepth} / {report.ReparsePointsSkipped}", "" }, new[] { "Errors", report.IssueCount.ToString(), "First 1,000 retained" }, new[] { "Duration", report.Duration.ToString(), "" } };

        rows.AddRange(report.LargestFiles.Select(f => new[] { "Large file", f.Bytes.ToString(), f.Path })); rows.AddRange(report.LargestDirectories.Select(f => new[] { "Directory (recursive)", f.Bytes.ToString(), f.Path })); rows.AddRange(report.Extensions.Select(e => new[] { "Extension", e.Value.ToString(), e.Key })); rows.AddRange(report.Issues.Select(e => new[] { "Error", e.Message, e.Path }));

        vm.SetTable(new("Directory analysis", ["Category", "Value / bytes", "Path / detail"], rows, "Logical sizes include hard links. Size and age do not make deletion safe. Reparse points are skipped; failures are reported."));

    });

    private Task Duplicates() => Work(async token =>

    {

        var report = await vm.Files.DuplicatesAsync(pathBox?.Text ?? directory, OperationProgress(), token); token.ThrowIfCancellationRequested();

        vm.SetTable(new("Duplicates", ["Group", "Bytes per file", "Path"], report.Groups.SelectMany((g, i) => g.Paths.Select(p => new[] { (i + 1).ToString(), g.BytesPerFile.ToString(), p })).ToArray(), $"Full content compared after SHA-256. Potential logical savings: {Calculations.Bytes(report.ReclaimableBytes)}; hard links excluded: {report.HardLinksSkipped}; errors/changed files: {report.Errors}; truncated: {report.Truncated}. No automatic deletion."));

    });

    private async Task FileAction()

    {

        if (vm.Table.Title != "Files" || DataTable.List.SelectedItem is not DisplayRow row || row.Cells[1] != "File") { vm.Status = "Select an individual file in a directory listing."; return; }

        var source = row.Cells[5]; var action = new ComboBox { ItemsSource = new[] { "Open", "Containing folder", "Properties", "Copy", "Move / rename", "Recycle", "Inspect file locks" }, SelectedIndex = 1 }; var destination = new TextBox { Header = "Full destination path", Text = source }; var stack = new StackPanel { Spacing = 8 }; stack.Children.Add(new TextBlock { Text = source, TextWrapping = TextWrapping.Wrap }); stack.Children.Add(action); stack.Children.Add(destination);

        if (await ShowDialog(new ContentDialog { Title = "File action", Content = stack, PrimaryButtonText = "Review", CloseButtonText = "Cancel", XamlRoot = Root.XamlRoot }) != ContentDialogResult.Primary) return;

        if (action.SelectedIndex == 6) { await Work(async t => { var locks = await Task.Run(() => vm.Desktop.FileUsers(source), t); await Dialog(locks.Title, new TextBox { Text = string.Join("\n", locks.Rows.Select(r => string.Join(" | ", r))) + "\n" + locks.Notice, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }); }); return; }

        if (action.SelectedIndex < 3) { if (action.SelectedIndex == 0) vm.Desktop.Open(source); else if (action.SelectedIndex == 1) vm.Desktop.ShowInFolder(source); else vm.Desktop.Properties(source); return; }

        var target = destination.Text; var selectedAction = action.SelectedIndex;

        if (!await Confirm("Confirm file operation", $"{action.SelectedItem}\nSource: {source}\nLogical size: {row.Cells[2]} bytes\nDestination: {(selectedAction == 5 ? "Recycle Bin" : target)}\nExisting destinations are never overwritten.")) return;

        await Work(token => Task.Run(() => { if (selectedAction == 5) vm.Files.Recycle(source); else if (selectedAction == 3) vm.Files.Copy(source, target); else vm.Files.Move(source, target); }, token)); await Refresh();

    }

}

