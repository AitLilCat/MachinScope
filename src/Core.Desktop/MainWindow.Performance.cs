using Core.Domain.Models;
using Core.Application.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
namespace Core.Desktop;
public sealed partial class MainWindow
{
    private bool discoveredResources, updatingResources, forceResourceDiscovery;
    private string[] discoveredNames = ["CPU", "Memory", "GPU", "Network"];
    private readonly Dictionary<string, string> resourceKeys = new();
    private ListView? resources;
    private Canvas? chart;
    private TextBlock? chartLabel;
    private Grid? chartHost;
    private readonly List<Polyline> chartLines = new();
    private readonly List<UIElement> chartAxes = new();
    private string axisSchema = "";
    private string performanceResource = "CPU";
    private readonly Dictionary<string, Queue<(DateTimeOffset Time, double? Value)>> charts = new();
    private void ClearPerformance() { if (resources != null) ContentArea.Children.Remove(resources); resources = null; chart = null; chartLabel = null; chartHost = null; chartLines.Clear(); chartAxes.Clear(); axisSchema = ""; ResourceColumn.Width = new(0); }
    private void BuildPerformanceToolbar()
    {
        ResourceColumn.Width = new(165); resources = new ListView { Width = 155, SelectionMode = ListViewSelectionMode.Single }; resources.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding}' ToolTipService.ToolTip='{Binding}' TextTrimming='CharacterEllipsis' FontSize='12'/></DataTemplate>"); resources.ItemsSource = discoveredNames; resources.SelectedItem = performanceResource; Grid.SetRow(resources, 1); Grid.SetRowSpan(resources, 3); ContentArea.Children.Add(resources);
        resources.SelectionChanged += async (_, _) => { if (!updatingResources && resources.SelectedItem is string resource) { performanceResource = resource; DrawChart(); await Refresh(); } };
        var period = new ComboBox { ItemsSource = new[] { "60 seconds", "5 minutes", "15 minutes" }, SelectedIndex = vm.Preferences.HistorySeconds == 300 ? 1 : vm.Preferences.HistorySeconds == 900 ? 2 : 0 }; period.SelectionChanged += (_, _) => { vm.SavePreferences(vm.Preferences with { HistorySeconds = period.SelectedIndex == 1 ? 300 : period.SelectedIndex == 2 ? 900 : 60 }); DrawChart(); }; Toolbar.Children.Add(period);
        FilterBar.Orientation = Orientation.Vertical; FilterBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        chartHost = (Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Height='190' HorizontalAlignment='Stretch' Background='{ThemeResource CoreSurfaceBackground}'/>");
        chartLabel = new TextBlock { FontSize = 12, Margin = new Thickness(8, 4, 8, 0) }; chart = new Canvas { Margin = new Thickness(0, 28, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        chartHost.Children.Add(chart); chartHost.Children.Add(chartLabel); FilterBar.Children.Add(chartHost); chart.SizeChanged += (_, _) => DrawChart();

    }
    private async Task RefreshPerformance(CancellationToken token)
    {
        var generation = navigationRevision.Current;
        if (!discoveredResources) { var storage = await vm.Storage.ReadAsync(forceResourceDiscovery, token); var adapters = await adapterCache.ReadAsync(ct => Task.Run(vm.Inspector.Adapters, ct), false, token); token.ThrowIfCancellationRequested(); if (!navigationRevision.IsCurrent(generation) || page != "Performance") return; discoveredResources = true; forceResourceDiscovery = false; resourceKeys.Clear(); var names = new List<string> { "CPU", "Memory", "GPU" }; names.AddRange(storage.Inventory.Disks.Select(d => "Disk " + d.Target.Number)); names.Add("Network"); foreach (var row in adapters.Data ?? []) { var label = "NIC: " + row.Cells[0]; resourceKeys[label] = "Network " + row.Key; names.Add(label); } discoveredNames = names.ToArray(); updatingResources = true; try { resources!.ItemsSource = discoveredNames; resources.SelectedItem = performanceResource; } finally { updatingResources = false; } }
        var resource = performanceResource; var key = resourceKeys.GetValueOrDefault(resource, resource); var table = await vm.Inspector.ResourceAsync(key, token); token.ThrowIfCancellationRequested(); if (!navigationRevision.IsCurrent(generation) || page != "Performance" || resource != performanceResource) return;
        vm.SetTable(ReadablePerformance(table)); var last = vm.History.Values.LastOrDefault(); double? value = resource switch { "CPU" => last?.CpuPercent, "Memory" => last == null || last.TotalMemory == 0 ? null : 100d * (last.TotalMemory - last.AvailableMemory) / last.TotalMemory, "Network" => last == null ? null : last.ReceiveRate + last.SendRate, _ => null };
        if (key.StartsWith("Network ")) { var row = table.Data?.FirstOrDefault(); if (row != null && double.TryParse(row.Raw[15], System.Globalization.CultureInfo.InvariantCulture, out var rx) && double.TryParse(row.Raw[16], System.Globalization.CultureInfo.InvariantCulture, out var tx)) value = rx + tx; }
        if (resource.StartsWith("Disk ")) { var raw = table.Rows.FirstOrDefault(r => r[0] == "Active time")?.ElementAtOrDefault(1); if (double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var n)) value = n; }
        if (resource == "GPU") { var engines = table.Rows.Where(r => r[2] == "% per engine instance").Select(r => double.TryParse(r[1], out var n) ? (double?)n : null).Where(n => n != null).ToArray(); if (engines.Length > 0) value = engines.Max(); }
        if (!charts.TryGetValue(resource, out var values)) charts[resource] = values = new(); var now = DateTimeOffset.Now; values.Enqueue((now, value)); while (values.Count > 900 || values.TryPeek(out var first) && now - first.Time > TimeSpan.FromMinutes(15)) values.Dequeue(); DrawChart();
    }
    private static InspectionTable ReadablePerformance(InspectionTable source)
    {
        if (source.Columns.Length != 3) return source;
        var data = (source.Data ?? Core.Application.Services.TableRows.From(source)).Select(row => { var cells = row.Cells.ToArray(); if (cells[2] is "B" or "B/s" && double.TryParse(row.Raw[1], System.Globalization.CultureInfo.InvariantCulture, out var n)) { cells[1] = Calculations.Bytes(n) + (cells[2] == "B/s" ? "/s" : ""); cells[2] = cells[2] == "B" ? "bytes" : "bytes/second"; } return row with { Cells = cells }; }).ToArray();
        return source with { Rows = data.Select(r => r.Cells).ToArray(), Data = data };
    }
    private static Line GraphGridLine(double x1, double x2, double y1, double y2, double opacity)
    {
        var line = (Line)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Line xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stroke='{ThemeResource CoreGraphGrid}' StrokeThickness='1'/>");
        line.X1 = x1; line.X2 = x2; line.Y1 = y1; line.Y2 = y2; line.Opacity = opacity;
        return line;
    }
    private void DrawChart()
    {
        if (chart == null || chartLabel == null || chart.ActualWidth < 100) return;
        var now = DateTimeOffset.Now; var seconds = vm.Preferences.HistorySeconds;
        var systemResource = performanceResource is "CPU" or "Memory" or "Network";
        var values = systemResource ? vm.History.Values.Where(s => (now - s.Time).TotalSeconds <= seconds).Select(s => (Time: s.Time, Value: performanceResource == "CPU" ? s.CpuPercent : performanceResource == "Memory" ? s.TotalMemory == 0 ? null : (double?)(100d * (s.TotalMemory - s.AvailableMemory) / s.TotalMemory) : s.ReceiveRate + s.SendRate)).ToArray() : charts.GetValueOrDefault(performanceResource)?.Where(v => (now - v.Time).TotalSeconds <= seconds).ToArray() ?? [];
        var points = values.Where(v => v.Value.HasValue).ToArray(); var traffic = performanceResource == "Network" || performanceResource.StartsWith("NIC: ");
        var max = traffic ? GraphMath.RateCeiling(points.Length == 0 ? 0 : points.Max(v => v.Value!.Value)) : 100;
        var caption = traffic ? "Receive + send" : performanceResource == "GPU" ? "Busiest engine instance" : performanceResource == "Memory" ? "Physical memory used" : performanceResource == "CPU" ? "Total CPU" : "Disk active time";
        var last = points.LastOrDefault().Value;
        chartLabel.Text = $"{caption} · {(last.HasValue ? traffic ? Calculations.Bytes(last.Value) + "/s" : last.Value.ToString("0.0") + "%" : "waiting for samples")} · {seconds}s history";
        var width = Math.Max(1, chart.ActualWidth - 70); var height = Math.Max(1, chart.ActualHeight - 30); const double left = 54;
        var schema = $"{width}|{height}|{max}|{seconds}|{traffic}";
        if (schema != axisSchema)
        {
            axisSchema = schema; foreach (var axis in chartAxes) chart.Children.Remove(axis); chartAxes.Clear();
            void Axis(UIElement element) { chartAxes.Add(element); chart.Children.Add(element); }
            for (var i = 0; i <= 4; i++) { var y = height * i / 4; Axis(GraphGridLine(left, left + width, y, y, 0.45)); var text = new TextBlock { Text = traffic ? Calculations.Bytes(max * (4 - i) / 4) : (max * (4 - i) / 4).ToString("0") + "%", FontSize = 10 }; Canvas.SetTop(text, Math.Max(0, y - 7)); Canvas.SetLeft(text, 2); Axis(text); }
            for (var i = 0; i <= 4; i++) { var x = left + width * i / 4; Axis(GraphGridLine(x, x, 0, height, 0.3)); }
            var past = new TextBlock { Text = seconds >= 60 ? $"−{seconds / 60} min" : $"−{seconds}s", FontSize = 10 }; Canvas.SetLeft(past, left); Canvas.SetTop(past, height + 4); Axis(past); var current = new TextBlock { Text = "Now", FontSize = 10 }; Canvas.SetLeft(current, left + width - 25); Canvas.SetTop(current, height + 4); Axis(current);
        }
        var segments = GraphMath.Segments(values, now, seconds, vm.Preferences.RefreshSeconds * 3 + 10);
        while (chartLines.Count < segments.Count) { var line = (Polyline)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Polyline xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stroke='{ThemeResource CoreGraphLine}' StrokeThickness='2'/>"); chartLines.Add(line); chart.Children.Add(line); }
        for (var i = 0; i < chartLines.Count; i++) { var line = chartLines[i]; line.Points.Clear(); line.Visibility = i < segments.Count ? Visibility.Visible : Visibility.Collapsed; if (i >= segments.Count) continue; foreach (var point in segments[i]) line.Points.Add(new global::Windows.Foundation.Point(left + width * point.X, height * (1 - Math.Clamp(point.Value / max, 0, 1)))); }
    }
}
