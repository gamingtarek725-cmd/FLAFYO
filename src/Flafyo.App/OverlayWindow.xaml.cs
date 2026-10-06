using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    private OverlayStyle _style = new();
    private readonly DispatcherTimer _hideTimer = new();

    public OverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd); // click-through + never takes focus + hidden from capture
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        ApplyStyle(_style);
    }

    private static SolidColorBrush MakeBrush(string hex, int alphaPercent = 100)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex); } catch { c = Colors.White; }
        c.A = (byte)(255 * Math.Clamp(alphaPercent, 0, 100) / 100);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void ApplyStyle(OverlayStyle s)
    {
        _style = s;
        try { Label.FontFamily = new FontFamily(s.FontFamily); Original.FontFamily = Label.FontFamily; }
        catch { Label.FontFamily = new FontFamily("Segoe UI"); }
        Label.FontSize = Math.Clamp(s.FontSize, 8, 120);
        Label.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
        Label.Foreground = MakeBrush(s.TextColor);
        Label.TextAlignment = s.Align switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, _ => TextAlignment.Left };   // start / center / end of the line
        Label.Effect = s.Shadow
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 0.95 }
            : null;
        Original.FontSize = Math.Max(10, Label.FontSize * 0.6);
        Original.Foreground = MakeBrush(s.TextColor, 70);
        Card.Background = MakeBrush(s.BackColor, s.BackOpacity);
        Card.BorderBrush = MakeBrush(s.BorderColor);
        Card.BorderThickness = new Thickness(s.Border ? 1.5 : 0);
    }

    public static System.Drawing.Rectangle DefaultPreviewRegion()
    {
        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
        return new System.Drawing.Rectangle(w / 2 - 400, h * 3 / 4, 800, 120);
    }

    public void ShowPreview(System.Drawing.Rectangle region) =>
        ShowText("هذا مثال لشكل الترجمة داخل اللعبة. غيّر الخط واللون والحجم كما تحب.",
                 "This is a sample of how the translation will look.", region, 6);

    /// <summary>Shows the translation inside the screen. regionPx = the text region in physical pixels. hideAfter: -1 = use the style's setting.</summary>
    public void ShowText(string text, string? original, System.Drawing.Rectangle regionPx, int hideAfter = -1)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        double sl = GetSystemMetrics(SM_XVIRTUALSCREEN) / scale, st = GetSystemMetrics(SM_YVIRTUALSCREEN) / scale;
        double sw = GetSystemMetrics(SM_CXVIRTUALSCREEN) / scale, sh = GetSystemMetrics(SM_CYVIRTUALSCREEN) / scale;
        const double margin = 8;
        var s = _style;

        Label.Text = text;
        Original.Text = original ?? "";
        Original.Visibility = s.ShowOriginal && !string.IsNullOrWhiteSpace(original) ? Visibility.Visible : Visibility.Collapsed;

        bool fixedArea = s.OverlayMode == 2 && s.AreaW >= 100;
        double w = fixedArea
            ? s.AreaW / scale
            : s.AutoWidthPx > 0 ? s.AutoWidthPx / scale : Math.Max(regionPx.Width / scale, 420);
        w = Math.Min(w, sw - 2 * margin);
        Width = w;
        MaxHeight = fixedArea && s.AreaH >= 40 ? Math.Min(s.AreaH / scale, sh - 2 * margin) : sh - 2 * margin;
        double left = fixedArea ? s.AreaX / scale : regionPx.X / scale;
        Left = Math.Max(sl + margin, Math.Min(left, sl + sw - w - margin));

        if (!IsVisible) Show();      // ShowActivated=false → does not steal focus
        UpdateLayout();

        double h = ActualHeight, top;
        if (fixedArea) top = s.AreaY / scale;
        else
        {
            double regionTop = regionPx.Y / scale, regionBottom = regionPx.Bottom / scale;
            bool below = s.OverlayMode == 1;
            top = below ? regionBottom + margin : regionTop - h - margin;
            if (!below && top < st + margin) { top = regionBottom + margin; if (top + h > st + sh - margin) top = regionTop + margin; }
            if (below && top + h > st + sh - margin) { top = regionTop - h - margin; if (top < st + margin) top = regionTop + margin; }
        }
        Top = Math.Max(st + margin, Math.Min(top, st + sh - h - margin));

        _hideTimer.Stop();
        int seconds = hideAfter >= 0 ? hideAfter : s.AutoHideSeconds;
        if (seconds > 0) { _hideTimer.Interval = TimeSpan.FromSeconds(seconds); _hideTimer.Start(); }
    }

    public new void Hide() { _hideTimer.Stop(); base.Hide(); }
}

public sealed class WpfOverlaySink : IOverlaySink
{
    private readonly OverlayWindow _window;
    private readonly Func<System.Drawing.Rectangle> _region;

    public WpfOverlaySink(OverlayWindow window, Func<System.Drawing.Rectangle> region) { _window = window; _region = region; }

    // InvokeAsync: the pipeline never waits for the UI thread.
    public void Show(int regionId, string translated) => Show(regionId, translated, "");

    public void Show(int regionId, string translated, string source) =>
        _window.Dispatcher.InvokeAsync(() => _window.ShowText(translated, source, _region()));

    public void Clear(int regionId) => _window.Dispatcher.InvokeAsync(() => _window.Hide());
}
