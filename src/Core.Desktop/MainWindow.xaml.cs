using Core.Application.Services;

using Core.Desktop.Controls;

using Core.Desktop.ViewModels;

using Core.Domain.Models;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

using Microsoft.UI.Xaml.Controls.Primitives;

using Windows.Storage.Pickers;

namespace Core.Desktop;

public sealed partial class MainWindow : Window

{

    private readonly ShellViewModel vm;

    private readonly CancellationTokenSource lifetime = new();

    private readonly SemaphoreSlim operations = new(1, 1);

    private readonly RefreshCoordinator refreshes;

    private readonly NavigationRevision navigationRevision = new();

    private readonly HashSet<ContentDialog> dialogs = new();

    private TaskCompletionSource? workCompletion;

    private bool closing, closeReady, refreshAfterWork;

    private readonly DispatcherTimer timer = new();

    private CancellationTokenSource? operation;

    private string page = "Overview", networkView = "Connections", directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private bool active = true, initialized;

    private TextBox? pathBox;
    private TextBox? activeFilter;

    private FrameworkElement? settingsHost;

    private ProcessSample? SelectedProcess => DataTable.SelectedRows.Select(r => r.Subject).OfType<ProcessSample>().FirstOrDefault();

    public MainWindow(ShellViewModel viewModel)

    {

        InitializeComponent(); vm = viewModel; vm.Measurements.Enabled = vm.Preferences.LocalDiagnostics; refreshes = new(vm.Measurements); DataTable.Diagnostics = vm.Measurements; InitializeChrome();

        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1400, 820));

        DataTable.LoadLayout = title => vm.Preferences.Tables.GetValueOrDefault(title);

        DataTable.SaveLayout = (title, columns) => { try { var layouts = new Dictionary<string, ColumnLayout[]>(vm.Preferences.Tables) { [title] = columns }; vm.SavePreferences(vm.Preferences with { Tables = layouts }); } catch (Exception e) { vm.Error(e); } };

        DataTable.Error += message => vm.Status = message;
        DataTable.SearchChanged += query => { if (activeFilter is { } box && box.Text != query) box.Text = query; };

        DataTable.Changed += () => { if (page is "Category" or "Settings") return; Status.Text = $"{DataTable.SelectedCount} selected · {DataTable.VisibleCount:N0} visible · Ctrl+A select · Ctrl+C copy · Right-click actions"; };

        DataTable.RowInvoked += async row => { try { if (page == "Processes" && row.Subject is ProcessSample process) await InspectProcess(process); else if (page == "Network") await NetworkProperties(row); else if (page is "Storage" or "Health" && row.Subject is DiskTarget disk) await DiskProperties(disk); else if (page == "Storage" && row.Subject is VolumeTarget volume) await VolumeProperties(volume); else if (page == "Devices" && row.Subject is DeviceTarget device) await DeviceProperties(device); else if (page == "Files" && vm.Table.Title == "Files") { if (row.Cells[1] == "Directory") await NavigateDirectory(row.Cells[5]); else if (row.Cells[1] == "File") await FileProperties(row.Raw[5]); } } catch (Exception e) { vm.Error(e); } };

        DataTable.ObjectMenu = ContextMenu;

        vm.PropertyChanged += OnViewModelChanged;

        refreshes.Failed += error => { vm.Error(error); if (page is not "Category" and not "Settings") DataTable.SetFailure(error.Message); };

        InitializeNavigation();
        var backKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Left, Modifiers = global::Windows.System.VirtualKeyModifiers.Menu };
        backKey.Invoked += (_, args) => { OnHistoryBack(this, new RoutedEventArgs()); args.Handled = true; }; Root.KeyboardAccelerators.Add(backKey);
        var forwardKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Right, Modifiers = global::Windows.System.VirtualKeyModifiers.Menu };
        forwardKey.Invoked += (_, args) => { OnHistoryForward(this, new RoutedEventArgs()); args.Handled = true; }; Root.KeyboardAccelerators.Add(forwardKey);
        ApplyTheme(); timer.Interval = TimeSpan.FromSeconds(vm.Preferences.RefreshSeconds); timer.Tick += async (_, _) => await Tick();

        Activated += (_, e) => active = e.WindowActivationState != WindowActivationState.Deactivated;

        AppWindow.Closing += async (_, args) => { if (closeReady) return; args.Cancel = true; if (closing) return; closing = true; await ShutdownAsync(); closeReady = true; Close(); };

        Root.Loaded += async (_, _) => { if (initialized) return; initialized = true; RestoreNavigation(); timer.Start(); await Tick(); };

    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)

    {

        if (closing) return;

        if (e.PropertyName == nameof(vm.Summary)) Summary.Text = vm.Summary;
        if (e.PropertyName == nameof(vm.Status)) Status.Text = vm.Status;

        if (e.PropertyName == nameof(vm.Table)) { Notice.Text = vm.Table.Notice ?? ""; _ = DataTable.SetTableAsync(vm.Table); }

    }

    private async Task ShutdownAsync()

    {

        timer.Stop(); navigationSave.Stop(); PersistNavigation(); navigationRevision.Advance(); lifetime.Cancel(); operation?.Cancel();

        foreach (var dialog in dialogs.ToArray()) dialog.Hide();

        vm.PropertyChanged -= OnViewModelChanged;

        await refreshes.DisposeAsync();

        if (workCompletion != null) await workCompletion.Task;

        await DataTable.StopAsync();
        await inventoryCache.DisposeAsync(); await adapterCache.DisposeAsync(); await endpointCache.DisposeAsync();

        operation?.Dispose(); operation = null; lifetime.Dispose(); operations.Dispose();

    }

    private void ApplyTheme() { Root.RequestedTheme = vm.Preferences.Theme switch { "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default }; UpdateChrome(); }

    private Task Tick()

    {

        if (closing || (!active && vm.Preferences.PauseWhenInactive) || operations.CurrentCount == 0 || refreshes.IsBusy) return Task.CompletedTask;

        var generation = navigationRevision.Current; var target = page; var view = networkView; var policy = NavigationRegistry.Get(routes.Selected).Refresh;

        return refreshes.Request(async token =>

        {

            var update = await vm.CollectAsync(policy is NavigationRefresh.Processes or NavigationRefresh.ActivitySummary, token);

            token.ThrowIfCancellationRequested(); if (!navigationRevision.IsCurrent(generation)) return;

            vm.Update(update, target == "Processes");

            if (policy == NavigationRefresh.Network) { var table = await ReadNetwork(view, false, token); token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation) && networkView == view) vm.SetTable(table); }
            if (target == "Category") UpdateHomepageSummary();

            if (policy == NavigationRefresh.Performance) await RefreshPerformance(token);

            if (policy == NavigationRefresh.Devices) { var deviceScope = deviceView; var table = await ReadDevices(false, token); token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation) && deviceView == deviceScope) vm.SetTable(table); }

        });

    }

    private async void OnNavigation(NavigationView sender, NavigationViewSelectionChangedEventArgs args)

    {

        if (closing || vm == null || args.SelectedItem is not NavigationViewItem item || item.Tag is not NavigationDestination destination) return;

        try

        {

            if (!routes.Select(destination, !restoringNavigation)) return;

            if (currentDestination is { } previous) inspectorQueries[previous] = (DataTable.Search, DataTable.ModeQuery);

            currentDestination = destination;

            if (!restoringNavigation) SyncExpansion();

            QueueNavigationSave();

            var route = NavigationRegistry.Get(destination);

            navigationRevision.Advance(); _ = refreshes.CancelAndDrain(); operation?.Cancel(); page = route.LegacyPage; DataTable.PreserveSourceOrder = page == "Processes" && vm.Tree || page == "Devices" && deviceView == "Events"; activeFilter = null; Toolbar.Children.Clear(); FilterBar.Children.Clear(); FilterBar.Orientation = Orientation.Horizontal; ClearPerformance(); DataTable.Search = ""; DataTable.ModeQuery = ""; vm.SetTable(new("", [], []));

            if (settingsHost != null) { ContentArea.Children.Remove(settingsHost); settingsHost = null; } DataTable.Visibility = Visibility.Visible; FilterBar.Visibility = Visibility.Visible;

            if (page == "Settings") { Settings(); return; }

            if (route.Homepage) { ShowHomepage(route); await Refresh(); return; }

            DataTable.SetLoading();
            if (page == "Files") BuildFilesToolbar();

            else

            {

                var search = new TextBox { PlaceholderText = page == "Processes" ? "Filter processes…  cpu>10 memory>500MB" : page == "Network" ? "Filter endpoints…  protocol:tcp port:443" : page == "Devices" ? "Filter devices…  vendor:046d class:USB" : "Filter rows…", Width = 380, Text = inspectorQueries.GetValueOrDefault(destination).Search ?? "" };

                activeFilter = search; search.TextChanged += (_, _) => DataTable.Search = search.Text; Toolbar.Children.Add(search);

                AddButton("Refresh", ManualRefresh); AddButton("Export", Export); AddButton("Columns", () => DataTable.ConfigureColumnsAsync(Root.XamlRoot));
                if (page is "Overview" or "Security") AddButton("Windows administration…", () => { OpenAdministrationCatalog(page == "Security" ? AdministrationGroup.SecurityUtilities : AdministrationGroup.AdvancedConfiguration); return Task.CompletedTask; });

                if (page == "Processes") { Chips(("All", ""), ("Active", "active=yes"), ("High CPU", "cpu>10"), ("High RAM", "memory>500MB")); var tree = new CheckBox { Content = "Tree", IsChecked = vm.Tree }; tree.Click += (_, _) => { vm.Tree = tree.IsChecked == true; DataTable.PreserveSourceOrder = vm.Tree; vm.ShowProcesses(); }; Toolbar.Children.Add(tree); AddButton("Details", Details); AddButton("Control…", ProcessControl); AddButton("Launch…", Launch); }

                if (page == "Startup") { var views = new ComboBox { ItemsSource = new[] { "Startup applications", "System services" }, SelectedIndex = servicesView ? 1 : 0 }; views.SelectionChanged += async (_, _) => { servicesView = views.SelectedIndex == 1; DataTable.ModeQuery = ""; await Refresh(); }; Toolbar.Children.Add(views); }
                if (page == "Storage") Chips(("Physical disks", "kind=\"Physical disk\""), ("Partitions", "kind=Partition"), ("Volumes", "kind=Volume"), ("Mapping / provider", ""));

                if (page == "Network") { networkView = networkStates.GetValueOrDefault(destination, route.View ?? "Connections"); var views = new ComboBox { ItemsSource = new[] { "Connections", "Interfaces", "Traffic", "DNS", "Timeline" }, SelectedItem = networkView }; views.SelectionChanged += async (_, _) => { networkView = views.SelectedItem.ToString()!; var next = networkView switch { "Connections" => NavigationDestination.Connections, "Interfaces" => NavigationDestination.Adapters, "Traffic" => NavigationDestination.Traffic, "DNS" => NavigationDestination.Adapters, "Timeline" => NavigationDestination.Connections, _ => destination }; if (next != destination) { networkStates[next] = networkView; NavigateTo(next); return; } networkStates[destination] = networkView; DataTable.ModeQuery = ""; FilterBar.Visibility = networkView == "Connections" ? Visibility.Visible : Visibility.Collapsed; await Refresh(); }; Toolbar.Children.Add(views); AddButton("Diagnose…", Diagnose); Chips(("All", ""), ("Incoming", "direction=Incoming"), ("Outgoing", "direction=Outgoing"), ("Listening", "state=Listen"), ("Established", "state=Established")); }

                if (page == "Performance") BuildPerformanceToolbar();

                if (page == "Devices") BuildDeviceToolbar();

            }

            if (inspectorQueries.TryGetValue(destination, out var saved)) { DataTable.Search = saved.Search; SetChipQuery(saved.Mode); }


            if (page == "Network") FilterBar.Visibility = networkView == "Connections" ? Visibility.Visible : Visibility.Collapsed;

            await Refresh();

        }

        catch (Exception e) { vm.Error(e); }

    }

    private void AddButton(string label, Func<Task> action)

    {

        var b = new Button { Content = label, FontSize = 12, Padding = new Thickness(8, 5, 8, 5) }; b.Click += async (_, _) => { try { await action(); } catch (Exception e) { vm.Error(e); } }; Toolbar.Children.Add(b);

    }

    private void Chips(params (string Label, string Query)[] choices)

    {

        var buttons = new List<ToggleButton>();

        foreach (var choice in choices) { var b = new ToggleButton { Content = choice.Label, Tag = choice.Query, IsChecked = buttons.Count == 0, FontSize = 12, Padding = new Thickness(12, 3, 12, 3) }; buttons.Add(b); b.Click += (_, _) => { foreach (var other in buttons) other.IsChecked = other == b; DataTable.ModeQuery = choice.Query; }; if (page == "Network" && choice.Label is "Incoming" or "Outgoing") { b.IsEnabled = false; ToolTipService.SetToolTip(b, "Initiator direction requires reliable event capture; current snapshots report Unknown."); } FilterBar.Children.Add(b); }

        DataTable.ModeQuery = choices[0].Query;

    }

    private async Task Work(Func<CancellationToken, Task> action)

    {

        if (closing) return;

        if (!await operations.WaitAsync(0)) { vm.Status = "An operation is already running. Wait or cancel it first."; return; }

        workCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var generation = navigationRevision.Current; var target = page;

        var started = vm.Measurements.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        var outcome = RefreshState.Stale;

        try

        {

            await refreshes.CancelAndDrain();

            if (closing || !navigationRevision.IsCurrent(generation)) return;

            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);

            vm.Status = "Working…";

            if (target == "Files" && fileCancel != null) fileCancel.Visibility = Visibility.Visible;

            await action(operation.Token);

            outcome = operation.IsCancellationRequested ? RefreshState.Cancelled : RefreshState.Applied;

            if (!closing && navigationRevision.IsCurrent(generation)) vm.Status = outcome == RefreshState.Cancelled ? OperationErrors.Classify(new OperationCanceledException()).Message : $"Updated {DateTime.Now:T}";

        }

        catch (Exception e) { outcome = e is OperationCanceledException ? RefreshState.Cancelled : RefreshState.Failed; vm.Error(e); }

        finally

        {

            if (started != 0) vm.Measurements.Record(DiagnosticMetric.Operation, System.Diagnostics.Stopwatch.GetElapsedTime(started), outcome);

            operation?.Dispose(); operation = null;

            if (fileCancel != null) fileCancel.Visibility = Visibility.Collapsed;

            operations.Release();

            var done = workCompletion; workCompletion = null; done!.TrySetResult();

            if (refreshAfterWork && !closing) { refreshAfterWork = false; _ = Refresh(); }

        }

    }

    private bool servicesView;
    private Task Refresh() => RefreshCore(false);
    private Task ManualRefresh() { if (page == "Performance") { discoveredResources = false; forceResourceDiscovery = true; } return RefreshCore(true); }
    private Task RefreshCore(bool force)

    {

        if (closing) return Task.CompletedTask;

        if (operations.CurrentCount == 0) { refreshAfterWork = true; return Task.CompletedTask; }

        var generation = navigationRevision.Advance(); var target = page; var targetDirectory = directory; var view = networkView; var destination = routes.Selected; var serviceScope = servicesView;

        return refreshes.Request(async token => {

        if (target == "Category") { await LoadHomepage(destination, generation, force, token); return; }
        if (target == "Processes") { var processes = await Task.Run(vm.Inspector.Processes, token); token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation)) vm.SetProcesses(processes); return; }

        if (target == "Performance") { await RefreshPerformance(token); return; }

        var table = target switch { "Overview" => await inventoryCache.ReadAsync(vm.Inspector.OverviewAsync, force, token), "Security" => ReadSecurity(), "Storage" => await ReadStorage(force, token), "Health" => await ReadDiskHealth(force, token), "Devices" => await ReadDevices(force, token), "Startup" => serviceScope ? await vm.Inspector.ServicesAsync(token) : await vm.Inspector.StartupAsync(token), "Files" => await Task.Run(() => vm.Files.List(targetDirectory, "", token), token), "Network" => await ReadNetwork(view, force, token), _ => null };

        token.ThrowIfCancellationRequested(); if (navigationRevision.IsCurrent(generation) && table != null && (target != "Files" || directory == targetDirectory)) vm.SetTable(table);

        }, replaceActive: true);

    }

    private IProgress<string> OperationProgress()

    {

        var generation = navigationRevision.Current;

        return new Progress<string>(message => { if (!closing && navigationRevision.IsCurrent(generation)) vm.Status = message; });

    }

    private Task Export() => ExportCore(0);

    private Task ExportSelected() => ExportCore(1);

    private async Task ExportCore(int initialScope)

    {

        var scope = new ComboBox { ItemsSource = new[] { "Visible rows", "Selected visible rows", "Complete snapshot" }, SelectedIndex = initialScope };

        if (await ShowDialog(new ContentDialog { Title = "Export scope · visible columns, raw values", Content = scope, PrimaryButtonText = "Choose file", CloseButtonText = "Cancel", XamlRoot = Root.XamlRoot }) != ContentDialogResult.Primary) return;

        var exported = (scope.SelectedIndex == 1 ? DataTable.SelectedRows : scope.SelectedIndex == 2 ? DataTable.CompleteRows : DataTable.VisibleRows).ToArray(); var columns = DataTable.VisibleColumns.ToArray(); var table = vm.Table;

        var picker = new FileSavePicker { SuggestedFileName = $"MachinScope-{page}-{DateTime.Now:yyyyMMdd-HHmmss}" }; picker.FileTypeChoices.Add("CSV", [".csv"]); picker.FileTypeChoices.Add("JSON", [".json"]);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); var file = await picker.PickSaveFileAsync(); if (file == null) return;

        await Work(token => ExportService.WriteRowsAsync(file.Path, table.Title, table.Columns, exported, columns, token));

    }

    private Task Dialog(string title, object content) => InspectValues(title, content as UIElement ?? new TextBlock { Text = content.ToString(), TextWrapping = TextWrapping.Wrap });

    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)

    {

        if (closing) return ContentDialogResult.None;

        if (dialogs.Count != 0) { vm.Status = "Close the current dialog before opening another."; return ContentDialogResult.None; }

        dialogs.Add(dialog);

        try { return await dialog.ShowAsync(); } finally { dialogs.Remove(dialog); }

    }

    private async Task<bool> Confirm(string title, string body) => await ShowDialog(new ContentDialog { Title = title, Content = new ScrollViewer { Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }, MaxHeight = 450 }, PrimaryButtonText = "Confirm", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot }) == ContentDialogResult.Primary;

    private MenuFlyout ContextMenu(IReadOnlyList<InspectionRow> selected, InspectionRow? row, int column)

    {

        var menu = new MenuFlyout();

        void Item(string text, Func<Task> action) { var item = new MenuFlyoutItem { Text = text }; item.Click += async (_, _) => { try { await action(); } catch (Exception e) { vm.Error(e); } }; menu.Items.Add(item); }

        void Unavailable(string text, string reason) { var item = new MenuFlyoutItem { Text = text + " · unavailable", IsEnabled = false }; ToolTipService.SetToolTip(item, reason); menu.Items.Add(item); }

        if (page == "Processes") { if (row?.Subject is ProcessSample process) Item("Process details", () => InspectProcess(process)); Item("Control selected processes…", ProcessControl); Unavailable("Suspend / resume", "Whole-process suspend/resume requires a supported atomic provider. Thread-by-thread mutation can deadlock the target."); }

        if (page == "Startup") {

            if (!vm.Preferences.StartupControls) Unavailable("Enable / disable startup", "Enable startup controls in MachinScope Preferences.");

            else if (row?.Subject is StartupEntry entry) Item(entry.Disabled ? "Restore startup entry…" : "Disable startup entry…", async () => { if (!await Confirm(entry.Disabled ? "Restore startup entry" : "Disable startup entry", $"Name: {entry.Name}\nCommand: {entry.Command}\nSource: HKCU\\{entry.RegistryPath}\nAffects future logon. The running process stays running. MachinScope retains a recovery backup when disabling.")) return; ActionItemResult? result = null;

                await Work(async t => { try { await Task.Run(() => vm.Inspector.SetStartupEnabled(entry, entry.Disabled), t); result = new(entry.Identity, ActionOutcome.Succeeded, "Requested state verified by the provider."); } catch (OperationCanceledException) { result = new(entry.Identity, ActionOutcome.Cancelled, "Inspect current state before retrying."); } catch (Exception e) { result = new(entry.Identity, ActionOutcome.Failed, e.Message + " Recovery backup is retained where available; inspect the entry before retrying."); } });

                if (result != null) await Dialog("Startup action result", new TextBlock { Text = $"{result.Identity}\n{result.Outcome}: {result.Message}", TextWrapping = TextWrapping.Wrap }); await Refresh(); });

            else Unavailable("Enable / disable startup", "Supported for current-user Run entries only. System services, RunOnce and other sources remain read-only.");

        }

        if (page == "Files") AddFileContext(menu, selected, row);

        if (page == "Devices") AddDeviceContext(menu, selected, row);

        if (page is "Storage" or "Health")

        {

            if (row?.Subject is DiskTarget disk)

            {

                Item("Show partitions", () => { NavigateTo(NavigationDestination.DisksVolumes); SetChipQuery("kind=Partition"); DataTable.Search = "\"Disk " + disk.Number + " /\""; return Task.CompletedTask; });


                Item("Export storage table…", Export);

                Item("Open Windows Disk Management", () => { vm.Desktop.OpenDiskUtility(); return Task.CompletedTask; });

                Item("Properties", () => DiskProperties(disk));

            }

            else if (row?.Subject is PartitionTarget partition)

            {

                foreach (var volume in partition.Volumes) Item("Open " + volume.Path, () => OpenVolumeInFiles(volume));

                Item("Properties", () => InspectValues("Partition", new TextBox { IsReadOnly = true, Text = $"{partition.DeviceId}\nDisk {partition.DiskNumber}\nType: {partition.Type}\nOffset: {partition.Offset} B\nCapacity: {partition.Capacity} B" }));

            }

            else if (row?.Subject is VolumeTarget volume)

            {

                Item("Open volume in Files", () => OpenVolumeInFiles(volume));


                Item("Properties", () => VolumeProperties(volume));

            }

        }

        if (page == "Network" && row?.Subject is ConnectionRow connection)

        {


            Item("Resolve remote address", async () => { if (connection.RemotePort.HasValue) await Work(async t => await Dialog("Reverse DNS", new TextBox { Text = await vm.Diagnostics.RunAsync("Reverse DNS", connection.RemoteAddress, 0, t), IsReadOnly = true })); });

            Item("View owner", () => InspectConnectionOwner(connection));

            Item("All connections for owner", () => { SetChipQuery(""); DataTable.Search = $"pid={connection.ProcessId}"; return Task.CompletedTask; });

            Item("End owner…", async () => { var owner = new ProcessSample(connection.ProcessId, 0, connection.ProcessName, connection.OwnerStarted, connection.Executable, null, null, null, null, null, "Unavailable", null, null, null); if (await Confirm("End connection owner", $"{owner.Name} · PID {owner.Id} · started {owner.Started:O}\nUnsaved data may be lost.")) await Work(t => Task.Run(() => vm.Inspector.EndProcess(owner, false), t)); });

            Unavailable("Close connection", "A tuple can be reused before SetTcpEntry. Atomic connection identity is unavailable.");
            Item("Properties", () => NetworkProperties(row!));

        }

        if (page == "Network" && row?.Subject is string or InspectionRow) Item("Properties", () => NetworkProperties(row!));
        return menu;

    }

    private InspectionTable ReadConnectionTimeline()

    {

        vm.Inspector.Connections();

        return ConnectionTimelineTable.Create(vm.Inspector.ConnectionEvents());

    }

    private async Task Diagnose()

    {

        var host = new TextBox { Header = "Hostname or address", Text = "localhost" }; var kind = new ComboBox { ItemsSource = new[] { "DNS", "Reverse DNS", "Ping", "TCP", "Traceroute" }, SelectedIndex = 0 }; var port = new NumberBox { Header = "TCP port", Value = 443, Minimum = 1, Maximum = 65535 }; var stack = new StackPanel { Spacing = 8 }; stack.Children.Add(kind); stack.Children.Add(host); stack.Children.Add(port);

        if (await ShowDialog(new ContentDialog { Title = "Network diagnostic", Content = stack, PrimaryButtonText = "Run", CloseButtonText = "Cancel", XamlRoot = Root.XamlRoot }) != ContentDialogResult.Primary) return;

        AddButton("Cancel", () => { operation?.Cancel(); return Task.CompletedTask; });

        await Work(async t => await Dialog("Result", new TextBox { Text = await vm.Diagnostics.RunAsync(kind.SelectedItem.ToString()!, host.Text, (int)port.Value, t), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 400 }));

    }

    private void Settings()

    {

        DataTable.Visibility = Visibility.Collapsed; Notice.Text = "Local-only preferences. Monitoring pauses while MachinScope is inactive.";

        var theme = new ComboBox { Header = "Theme", ItemsSource = new[] { "System", "Dark", "Light" }, SelectedItem = vm.Preferences.Theme }; var refresh = new ComboBox { Header = "Refresh seconds", ItemsSource = new[] { 1, 2, 5 }, SelectedItem = vm.Preferences.RefreshSeconds }; var hidden = new CheckBox { Content = "Include hidden files in scan", IsChecked = vm.Preferences.IncludeHiddenFiles }; var level = new ComboBox { Header = "Log level", ItemsSource = new[] { "Debug", "Information", "Warning", "Error" }, SelectedItem = vm.Preferences.LogLevel }; var retention = new NumberBox { Header = "Log retention days", Minimum = 1, Maximum = 90, Value = vm.Preferences.LogRetentionDays }; var stack = new StackPanel { Spacing = 14 }; stack.Children.Add(theme); stack.Children.Add(refresh); stack.Children.Add(hidden); stack.Children.Add(level); stack.Children.Add(retention); var startupControls = new CheckBox { Content = "Allow reviewed startup enable/disable actions", IsChecked = vm.Preferences.StartupControls }; var pause = new CheckBox { Content = "Pause live monitoring when MachinScope is inactive", IsChecked = vm.Preferences.PauseWhenInactive }; stack.Children.Add(startupControls); stack.Children.Add(pause);

        var measurements = new CheckBox { Content = "Record local performance diagnostics (bounded; no paths or identities)", IsChecked = vm.Preferences.LocalDiagnostics }; stack.Children.Add(measurements);

        var save = new Button { Content = "Save" }; save.Click += (_, _) => { try { vm.SavePreferences(vm.Preferences with { Theme = theme.SelectedItem.ToString()!, RefreshSeconds = (int)refresh.SelectedItem, IncludeHiddenFiles = hidden.IsChecked == true, LogLevel = level.SelectedItem.ToString()!, LogRetentionDays = (int)retention.Value, StartupControls = startupControls.IsChecked == true, PauseWhenInactive = pause.IsChecked == true, LocalDiagnostics = measurements.IsChecked == true }); ApplyTheme(); timer.Interval = TimeSpan.FromSeconds(vm.Preferences.RefreshSeconds); vm.Status = "Preferences saved."; } catch (Exception e) { vm.Error(e); } }; stack.Children.Add(save);

        var exportMeasurements = new Button { Content = "Export local performance report…" }; exportMeasurements.Click += async (_, _) => { try { var picker = new FileSavePicker { SuggestedFileName = "MachinScope-performance" }; picker.FileTypeChoices.Add("JSON", [".json"]); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); var file = await picker.PickSaveFileAsync(); if (file != null && !closing) { await vm.Measurements.ExportAsync(file.Path, lifetime.Token); vm.Status = "Exported local performance report."; } } catch (Exception e) { vm.Error(e); } }; stack.Children.Add(exportMeasurements);

        stack.Children.Add(new TextBlock { Text = "Columns are configured per inspector and saved locally. Keyboard: Alt+Left/Right for page history; Ctrl+A/C in tables. Native Windows scaling and high-contrast settings are followed.", TextWrapping = TextWrapping.Wrap });
        var reset = new Button { Content = "Reset preferences" }; reset.Click += (_, _) => { try { vm.SavePreferences(new()); ApplyTheme(); timer.Interval = TimeSpan.FromSeconds(2); vm.Status = "Preferences reset."; } catch (Exception e) { vm.Error(e); } }; stack.Children.Add(reset);

        stack.Children.Add(new TextBlock { Text = $"No telemetry, accounts or background service. Diagnostics make network requests only when invoked. Local logs may contain paths and process identifiers.\nData: {vm.Store.DataDirectory}", TextWrapping = TextWrapping.Wrap });

        settingsHost = new ScrollViewer { Content = stack, VerticalAlignment = VerticalAlignment.Top }; Grid.SetColumn(settingsHost, 1); Grid.SetRow(settingsHost, 3); ContentArea.Children.Add(settingsHost);

    }

}

