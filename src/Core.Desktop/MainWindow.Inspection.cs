using Core.Application.Services;
using Core.Desktop.Controls;
using Core.Domain.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace Core.Desktop;

public sealed partial class MainWindow
{
    private async Task ShowInspectionDialog(ContentDialog dialog, TabView? tabs, InspectionScope scope)
    {
        var context = new InspectionContext(scope, vm.Capabilities.Platform.Kind);
        if (tabs != null) foreach (var item in tabs.TabItems.OfType<TabViewItem>()) item.Tag = InspectionDescriptions.Resolve(scope, item.Header?.ToString() ?? "General").Id;
        var description = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0) };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
        close.Click += (_, _) => dialog.Hide();
        var footer = new Grid { MinHeight = 60, MaxHeight = 76, Margin = new(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(description); Grid.SetColumn(close, 1); footer.Children.Add(close);
        void Update() {
            context.SelectId((tabs?.SelectedItem as TabViewItem)?.Tag?.ToString() ?? (tabs?.TabItems.OfType<TabViewItem>().FirstOrDefault()?.Tag?.ToString() ?? InspectionDescriptions.Resolve(scope, "General").Id));
            description.Text = context.Current.ShortDescription;
            ToolTipService.SetToolTip(description, context.Current.ShortDescription + (context.Current.ExtendedExplanation is { } more ? "\n" + more : ""));
        }
        void SelectionChanged(object sender, SelectionChangedEventArgs args) => Update();
        var content = dialog.Content as UIElement ?? throw new InvalidOperationException("Inspection content must be a UI element.");
        dialog.Content = null;
        var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.Children.Add(content); Grid.SetRow(footer, 1); panel.Children.Add(footer);
        dialog.Content = panel; dialog.CloseButtonText = ""; dialog.RequestedTheme = Root.ActualTheme;
        // The content footer keeps the explanation left of Close without replacing the native dialog template.
        var escape = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) => { dialog.Hide(); args.Handled = true; }; dialog.KeyboardAccelerators.Add(escape);
        if (tabs != null) tabs.SelectionChanged += SelectionChanged;
        Update();
        try { await ShowDialog(dialog); }
        finally { if (tabs != null) tabs.SelectionChanged -= SelectionChanged; dialog.KeyboardAccelerators.Remove(escape); }
    }
    private Task InspectValues(string title, UIElement content, InspectionScope scope = InspectionScope.General) {
        var dialog = new ContentDialog { Title = title, Content = content, XamlRoot = Root.XamlRoot };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(Root.ActualWidth - 100, 320, 1120);
        return ShowInspectionDialog(dialog, null, scope);
    }
    private async Task InspectRowValues(string title, string[] columns, InspectionRow row) {
        var control = new InspectionTableControl { Width = Math.Clamp(Root.ActualWidth - 100, 320, 1120) - 48, Height = Math.Clamp(Root.ActualHeight - 300, 220, 500) };
        await control.SetTableAsync(new(title, ["Property", "Value"], columns.Select((name, i) => new[] { name, row.Cells.ElementAtOrDefault(i) ?? "Unavailable" }).ToArray()));
        await InspectValues(title, control);
    }
}
