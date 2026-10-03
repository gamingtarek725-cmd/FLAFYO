using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd); // click-through + never takes focus + hidden from capture
    }

    /// <summary>Places the translation just above the region (or below it if there is no room). regionPx is in screen pixels.</summary>
    public void ShowText(string text, System.Drawing.Rectangle regionPx)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        Label.Text = text;
        Width = Math.Max(regionPx.Width / scale, 420);
        Left = regionPx.X / scale;
        if (!IsVisible) Show();      // ShowActivated=false → does not steal focus
        UpdateLayout();
        double top = regionPx.Y / scale - ActualHeight - 8;
        if (top < 0) top = (regionPx.Bottom / scale) + 8;
        Top = top;
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
