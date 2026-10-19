using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace Core.Desktop;

public sealed partial class MainWindow
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct HighContrast { public uint Size, Flags; public IntPtr Scheme; }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool SystemParametersInfoW(uint action, uint size, ref HighContrast value, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetSysColor(int index);
    private static global::Windows.UI.Color SystemColor(int index) { var value = GetSysColor(index); return ColorHelper.FromArgb(255, (byte)value, (byte)(value >> 8), (byte)(value >> 16)); }
    private void InitializeChrome()
    {
        if (AppWindowTitleBar.IsCustomizationSupported()) { ExtendsContentIntoTitleBar = true; SetTitleBar(TitleDragRegion); }
        Root.ActualThemeChanged += (_, _) => UpdateChrome();
        Activated += (_, _) => UpdateChrome();
        UpdateChrome();
    }
    private void UpdateChrome()
    {
        var bar = AppWindow.TitleBar;
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var background = dark ? ColorHelper.FromArgb(255, 17, 17, 17) : ColorHelper.FromArgb(255, 250, 250, 250);
        var foreground = dark ? Colors.White : Colors.Black;
        TitleDragRegion.Background = new SolidColorBrush(background);
        var contrast = new HighContrast { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<HighContrast>() };
        var highContrast = SystemParametersInfoW(0x42, contrast.Size, ref contrast, 0) && (contrast.Flags & 1) != 0;
        ((Microsoft.UI.Xaml.Controls.TextBlock)TitleDragRegion.Children[0]).Foreground = new SolidColorBrush(highContrast ? SystemColor(8) : foreground);
        if (highContrast)
        {
            TitleDragRegion.Background = new SolidColorBrush(SystemColor(5));
            bar.BackgroundColor = bar.ForegroundColor = bar.ButtonBackgroundColor = bar.ButtonForegroundColor = bar.ButtonInactiveBackgroundColor = bar.ButtonInactiveForegroundColor = bar.ButtonHoverBackgroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedBackgroundColor = bar.ButtonPressedForegroundColor = null;
            bar.InactiveBackgroundColor = bar.InactiveForegroundColor = null;
            return;
        }
        bar.BackgroundColor = bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = background;
        bar.ForegroundColor = bar.ButtonForegroundColor = foreground;
        bar.InactiveBackgroundColor = background;
        bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        bar.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(255, 45, 45, 45) : ColorHelper.FromArgb(255, 230, 230, 230);
        bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(255, 60, 60, 60) : ColorHelper.FromArgb(255, 215, 215, 215);
    }
}
