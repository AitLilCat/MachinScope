using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Input;
namespace Core.Desktop.Controls;

public sealed class DisplayCell(string text) : INotifyPropertyChanged
{
    public string Text { get; private set; } = text;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(string next) { if (Text == next) return; Text = next; PropertyChanged?.Invoke(this, new(nameof(Text))); }
}
public sealed class DisplayRow(InspectionRow data)
{
    public InspectionRow Data { get; private set; } = data;
    public string Key => Data.Key;
    public string[] Cells => Data.Cells;
    public DisplayCell[] Values { get; } = data.Cells.Select(c => new DisplayCell(c)).ToArray();
    public void Update(InspectionRow next) { Data = next; for (var i = 0; i < Values.Length; i++) Values[i].Update(next.Cells.ElementAtOrDefault(i) ?? ""); }
}

/// <summary>Shared virtualized table, sticky synchronized headers, key-based selection and clipboard.</summary>
public sealed class InspectionTableControl : UserControl
{
    private readonly ReconciledCollection<DisplayRow> rows = new();
    private readonly TextBlock emptyState = new() { Margin = new Thickness(12), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
    private bool loading;
    private string? failure;
    public void SetLoading() { loading = true; failure = null; UpdateEmptyState(); }
    public void SetFailure(string message) { loading = false; failure = message; UpdateEmptyState(); }
    private void UpdateEmptyState() { emptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; emptyState.Text = failure ?? (loading ? "Loading inspection…" : complete.Count == 0 ? "No data returned. Check provider availability or refresh." : "No rows match the current filters."); }
    private readonly TableSelection selection = new();
    private readonly StackPanel headers = new() { Orientation = Orientation.Horizontal };
    private readonly Grid surface = new();
    private readonly ScrollViewer scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, HorizontalContentAlignment = HorizontalAlignment.Left };
    public ListView List { get; } = new() { SelectionMode = ListViewSelectionMode.Extended, IsDoubleTapEnabled = true };
    private readonly RefreshCoordinator filtering = new();
    private long revision;
    private bool reconciling;
    private string search = "", modeQuery = "", schema = "";
    private InspectionTable table = new("", [], []);
    private IReadOnlyList<InspectionRow> complete = [];
    private readonly List<SortColumn> sorts = new();
    private ColumnLayout[] layout = [];
    private bool explicitWidths, resizing;
    public IReadOnlyList<InspectionRow> VisibleRows => rows.Select(r => r.Data).ToArray();
    public int VisibleCount => rows.Count;
    public int SelectedCount => List.SelectedItems.Count;
    public IReadOnlyList<InspectionRow> SelectedRows => rows.Where(r => selection.Keys.Contains(r.Key)).Select(r => r.Data).ToArray();
    public IReadOnlyList<InspectionRow> CompleteRows => complete;
    public IReadOnlyList<int> VisibleColumns => layout.Where(c => c.Visible).OrderBy(c => c.Order).Select(c => Array.IndexOf(table.Columns, c.Name)).Where(i => i >= 0).ToArray();
    public bool PreserveSourceOrder { get; set; }
    public LocalDiagnostics? Diagnostics { get; set; }
    public Func<string, ColumnLayout[]?>? LoadLayout { get; set; }
    public Action<string, ColumnLayout[]>? SaveLayout { get; set; }
    public Func<IReadOnlyList<InspectionRow>, InspectionRow?, int, MenuFlyout>? ObjectMenu { get; set; }
    public event Action<InspectionRow>? RowInvoked;
    public event Action<string>? Error;
    public event Action? Changed;
    public event Action<string>? SearchChanged;
    public string Search { get => search; set { if (search == value) return; search = value; SearchChanged?.Invoke(value); _ = ApplyAsync(true); } }
    public string ModeQuery { get => modeQuery; set { if (modeQuery == value) return; modeQuery = value; _ = ApplyAsync(false); } }
    public InspectionTableControl()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        surface.HorizontalAlignment = HorizontalAlignment.Left; scroller.HorizontalAlignment = HorizontalAlignment.Stretch;
        List.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        surface.RowDefinitions.Add(new() { Height = GridLength.Auto }); surface.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        surface.Children.Add(headers); Grid.SetRow(List, 1); surface.Children.Add(List); Grid.SetRow(emptyState, 1); surface.Children.Add(emptyState); scroller.Content = surface; Content = scroller;
        scroller.SizeChanged += (_, _) => { surface.Height = Math.Max(0, scroller.ActualHeight - 18); if (!resizing) { schema = ""; BuildColumns(); } };
        List.ItemsSource = rows; List.HorizontalAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
        List.ItemContainerStyle = new Style(typeof(ListViewItem));
        List.ItemContainerStyle.Setters.Add(new Setter(ListViewItem.MinHeightProperty, 27d));
        List.ItemContainerStyle.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(0)));
        List.ItemContainerStyle.Setters.Add(new Setter(ListViewItem.MarginProperty, new Thickness(0)));
        List.ItemContainerStyle.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        List.SelectionChanged += (_, _) => { if (reconciling) return; selection.ReplaceVisible(rows.Select(r => r.Key), List.SelectedItems.Cast<DisplayRow>().Select(r => r.Key)); Changed?.Invoke(); };
        List.KeyDown += OnKey;
        List.DoubleTapped += (_, e) => { if (RowAt(e.OriginalSource) is { } row) RowInvoked?.Invoke(row.Data); };
        List.RightTapped += Context;
        Unloaded += (_, _) => { revision++; _ = filtering.CancelAndDrain(); };
        filtering.Failed += e => Error?.Invoke(e.Message);
    }
    private static DisplayRow? RowAt(object source) => source is FrameworkElement element ? element.DataContext as DisplayRow : null;
    public async Task SetTableAsync(InspectionTable value)
    {
        var changed = value.Title != table.Title || !value.Columns.SequenceEqual(table.Columns);
        table = value; complete = TableRows.From(value); if (value.Title.Length > 0) { loading = false; failure = null; }
        if (changed) { selection.Clear(); sorts.Clear(); schema = ""; var saved = LoadLayout?.Invoke(value.Title); explicitWidths = saved != null; resizing = false; layout = value.Columns.Select((c, i) => saved?.FirstOrDefault(x => x.Name == c) ?? new ColumnLayout(c, DefaultWidth(c, i), !IsExtraColumn(c), i)).ToArray(); BuildColumns(); }
        await ApplyAsync(false);
    }
    private static bool IsExtraColumn(string c) => new[] { "Parent PID", "User", "CPU time", "Private memory", "Commit size", "Handles", "Priority", "Started", "Executable", "Command line", "Architecture", "Integrity", "Access", "Observed since", "Interface", "Bytes sent", "Bytes received" }.Contains(c);
    private double DefaultWidth(string name, int index) => name is "Instance path" or "Parent instance" ? 380 : name == "Manufacturer" ? 180 : name == "Location" ? 200 : name == "Value" && table.Columns.Length == 3 ? 180 : name == "Unit / meaning" ? 240 : index == 0 ? 205 : name.Contains("Path") || name.Contains("Value") || name.Contains("Command") || name.Contains("Executable") || name.Contains("Model") ? 350 : name.Contains("address", StringComparison.OrdinalIgnoreCase) || name.Contains("Started") || name.Contains("Modified") ? 190 : 110;
    public Task StopAsync() { revision++; return filtering.DisposeAsync().AsTask(); }
    private Task ApplyAsync(bool debounce)
    {
        var version = ++revision; var dataset = complete; var query = string.Join(" ", new[] { modeQuery, search }.Where(s => s.Length != 0)); var sorting = sorts.ToArray();
        var preserveOrder = PreserveSourceOrder;
        return filtering.Request(async token => {
        var previousKeys = sorting.Length == 0 && !preserveOrder ? rows.Select(r => r.Key).ToArray() : null;
        var ownsReconciliation = false;
        try
        {
            if (debounce) await Task.Delay(200, token);
            var values = await Task.Run(() => { var result = TableRows.FilterSort(dataset, query, sorting, token); return previousKeys == null ? result : TableRows.KeepExistingOrder(result, previousKeys); }, token);
            token.ThrowIfCancellationRequested(); if (version != revision) return;
            reconciling = ownsReconciliation = true;
            var started = Diagnostics?.Enabled == true ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            selection.Reconcile(dataset.Select(r => r.Key));
            var cache = rows.ToDictionary(r => r.Key, StringComparer.Ordinal); var desired = values.Select(v => { if (cache.TryGetValue(v.Key, out var row)) { row.Update(v); return row; } return new DisplayRow(v); }).ToArray();
            var viewport = FindViewport(List); var offset = viewport?.VerticalOffset ?? 0;
            var reset = rows.Reconcile(desired);
            RestoreSelection();
            if (reset && viewport != null) DispatcherQueue.TryEnqueue(() => { if (version == revision) viewport.ChangeView(null, offset, null, true); });
            UpdateEmptyState(); Changed?.Invoke();
            if (started != 0) Diagnostics!.Record(DiagnosticMetric.TableReconciliation, System.Diagnostics.Stopwatch.GetElapsedTime(started), RefreshState.Applied);
        }
        finally { if (ownsReconciliation) reconciling = false; }
        }, replaceActive: true);
    }
    private void RestoreSelection()
    {
        var chosen = new HashSet<DisplayRow>(List.SelectedItems.Cast<DisplayRow>(), ReferenceEqualityComparer.Instance);
        foreach (var item in chosen) if (!selection.Keys.Contains(item.Key)) List.SelectedItems.Remove(item);
        foreach (var item in rows) if (selection.Keys.Contains(item.Key) && !chosen.Contains(item)) List.SelectedItems.Add(item);
    }
    private static ScrollViewer? FindViewport(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindViewport(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i)) is { } child) return child;
        return null;
    }
    private static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.C && Down(VirtualKey.Control)) { Copy(); e.Handled = true; }
        else if (e.Key == VirtualKey.A && Down(VirtualKey.Control)) { selection.All(rows.Select(r => r.Key)); reconciling = true; try { RestoreSelection(); } finally { reconciling = false; } Changed?.Invoke(); e.Handled = true; }
        else if (e.Key == VirtualKey.Escape) { selection.Clear(); List.SelectedItems.Clear(); Changed?.Invoke(); e.Handled = true; }
    }
    public void Copy() => PutClipboard(TableRows.Tsv(SelectedRows, table.Columns, Enumerable.Range(0, table.Columns.Length).ToArray(), false, false));
    public static void PutClipboard(string value) { var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data); }
    private void Context(object sender, RightTappedRoutedEventArgs e)
    {
        var row = RowAt(e.OriginalSource); if (row == null) return;
        if (!List.SelectedItems.Contains(row)) { List.SelectedItems.Clear(); List.SelectedItems.Add(row); }
        var col = e.OriginalSource is FrameworkElement f && f.Tag is string s && int.TryParse(s, out var index) ? index : -1;
        var menu = ObjectMenu?.Invoke(SelectedRows, row.Data, col) ?? new MenuFlyout();
        var properties = menu.Items.LastOrDefault() as MenuFlyoutItem;
        if (properties?.Text == "Properties") menu.Items.Remove(properties); else properties = null;
        if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        void Item(string text, Action action) { var m = new MenuFlyoutItem { Text = text }; m.Click += (_, _) => action(); menu.Items.Add(m); }
        Item("Copy", Copy);
        if (properties != null) { menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(properties); }
        menu.ShowAt(List, e.GetPosition(List)); e.Handled = true;
    }
    private void BuildColumns()
    {
        var key = string.Join("|", layout.OrderBy(c => c.Order)); if (key == schema) return; schema = key; headers.Children.Clear();
        var markup = new StringBuilder("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal'>");
        var width = 0d; var displayed = TableColumns.Display(layout, scroller.ActualWidth, explicitWidths);
        foreach (var c in displayed.Where(c => c.Visible).OrderBy(c => c.Order))
        {
            var index = Array.IndexOf(table.Columns, c.Name); if (index < 0) continue; var actualWidth = c.Width; width += actualWidth;
            var g = new Grid { Width = actualWidth }; var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Content = c.Name + (sorts.FirstOrDefault(s => s.Index == index) is { } sort ? sort.Descending ? " ↓" : " ↑" : " ↕"), HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 11, Padding = new Thickness(6, 2, 6, 2) };
            b.Click += async (_, _) => { var old = sorts.FirstOrDefault(s => s.Index == index); if (!Down(VirtualKey.Shift)) sorts.Clear(); else sorts.RemoveAll(s => s.Index == index); sorts.Add(new(index, old != null && !old.Descending)); schema = ""; BuildColumns(); await ApplyAsync(false); };
            g.Children.Add(b); var thumb = new Thumb { Width = 5, HorizontalAlignment = HorizontalAlignment.Right }; double resize = actualWidth;
            var title = table.Title;
            thumb.DragStarted += (_, _) => {
                resizing = true;
                // Freeze the displayed widths before applying the user's change. Spare space must not refill it.
                layout = displayed;
                explicitWidths = true;
            };
            thumb.DragDelta += (_, e) => { resize = Math.Clamp(resize + e.HorizontalChange, 60, 4000); g.Width = resize; };
            thumb.DragCompleted += (_, _) => { if (title != table.Title) return; resizing = false; layout = layout.Select(x => x.Name == c.Name ? x with { Width = resize } : x).ToArray(); SaveLayout?.Invoke(table.Title, layout); schema = ""; BuildColumns(); };
            g.Children.Add(thumb); headers.Children.Add(g);
            markup.Append($"<TextBlock Text='{{Binding Values[{index}].Text}}' ToolTipService.ToolTip='{{Binding Values[{index}].Text}}' Tag='{index}' Width='{actualWidth.ToString(CultureInfo.InvariantCulture)}' Padding='6,3' FontFamily='Consolas' FontSize='12' TextTrimming='CharacterEllipsis'/>");
        }
        surface.Width = Math.Max(120, width); markup.Append("</StackPanel></DataTemplate>"); List.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(markup.ToString());
    }
    public async Task ConfigureColumnsAsync(XamlRoot root)
    {
        var items = new ObservableCollection<ColumnLayout>(layout.OrderBy(c => c.Order)); var editor = new ListView { ItemsSource = items, CanReorderItems = true, AllowDrop = true, CanDragItems = true, Height = 380, Width = 430, SelectionMode = ListViewSelectionMode.None };
        editor.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal' Spacing='12'><TextBlock Text='↕' VerticalAlignment='Center'/><CheckBox Content='{Binding Name}' IsChecked='{Binding Visible, Mode=TwoWay}' Width='260'/><TextBlock Text='{Binding Width}' VerticalAlignment='Center'/></StackPanel></DataTemplate>");
        // Column records are immutable; capture visibility from a dedicated mutable editor model instead.
        var choices = new ObservableCollection<ColumnChoice>(items.Select(c => new ColumnChoice(c)));
        editor.ItemsSource = choices;
        var result = await new ContentDialog { Title = "Columns · drag to reorder; drag header edge to resize", Content = editor, PrimaryButtonText = "Save", SecondaryButtonText = "Reset", CloseButtonText = "Cancel", XamlRoot = root }.ShowAsync();
        if (result == ContentDialogResult.None) return;
        layout = result == ContentDialogResult.Secondary ? table.Columns.Select((c, i) => new ColumnLayout(c, DefaultWidth(c, i), !IsExtraColumn(c), i)).ToArray() : choices.Select((c, i) => new ColumnLayout(c.Name, c.Width, c.Visible, i)).ToArray();
        explicitWidths = true; SaveLayout?.Invoke(table.Title, layout); schema = ""; BuildColumns();
    }
    private sealed class ColumnChoice(ColumnLayout c) { public string Name { get; } = c.Name; public double Width { get; } = c.Width; public bool Visible { get; set; } = c.Visible; }
}
