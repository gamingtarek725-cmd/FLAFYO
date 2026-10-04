using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    public OverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd); // click-through + never takes focus + hidden from capture
    }

    /// <summary>
    /// Places the translation just above the region (or below it if there is no room), always fully inside the screen.
    /// regionPx is in physical screen pixels.
    /// </summary>
    public void ShowText(string text, System.Drawing.Rectangle regionPx)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;

        // screen bounds in DIPs, same coordinate system as Left/Top
        double sl = GetSystemMetrics(SM_XVIRTUALSCREEN) / scale, st = GetSystemMetrics(SM_YVIRTUALSCREEN) / scale;
        double sw = GetSystemMetrics(SM_CXVIRTUALSCREEN) / scale, sh = GetSystemMetrics(SM_CYVIRTUALSCREEN) / scale;
        const double margin = 8;

        Label.Text = text;
        double w = Math.Min(Math.Max(regionPx.Width / scale, 420), sw - 2 * margin);
        Width = w;
        MaxHeight = sh - 2 * margin;
        Left = Math.Max(sl + margin, Math.Min(regionPx.X / scale, sl + sw - w - margin));

        if (!IsVisible) Show();      // ShowActivated=false → does not steal focus
        UpdateLayout();

        double h = ActualHeight;
        double regionTop = regionPx.Y / scale, regionBottom = regionPx.Bottom / scale;
        double top = regionTop - h - margin;                                   // preferred: above the region
        if (top < st + margin)
        {
            top = regionBottom + margin;                                       // else below it
            if (top + h > st + sh - margin) top = regionTop + margin;          // else inside its top edge
        }
        Top = Math.Max(st + margin, Math.Min(top, st + sh - h - margin));
    }
}

public sealed class WpfOverlaySink : IOverlaySink
{
    private readonly OverlayWindow _window;
    private readonly Func<System.Drawing.Rectangle> _region;

    public WpfOverlaySink(OverlayWindow window, Func<System.Drawing.Rectangle> region) { _window = window; _region = region; }

    // InvokeAsync: the pipeline never waits for the UI thread.
    public void Show(int regionId, string translated) =>
        _window.Dispatcher.InvokeAsync(() => _window.ShowText(translated, _region()));

    public void Clear(int regionId) => _window.Dispatcher.InvokeAsync(() => _window.Hide());
}
