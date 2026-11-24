using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

/// <summary>Displays translated region text. This translation surface is excluded from OCR capture.</summary>
public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private readonly DispatcherTimer _hideTimer = new();
    private OverlayStyle _style = new();

    public OverlayWindow()
    {
        InitializeComponent();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd);
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        ApplyStyle(_style);
    }

    internal static SolidColorBrush MakeBrush(string hex, int alphaPercent = 100)
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
        try
        {
            Label.FontFamily = new FontFamily(s.FontFamily);
            Original.FontFamily = Label.FontFamily;
        }
        catch { Label.FontFamily = new FontFamily("Segoe UI"); }
        Label.FontSize = Math.Clamp(s.FontSize, 13, 34);
        Label.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.SemiBold;
        Label.Foreground = MakeBrush(s.TextColor);
        Label.TextAlignment = s.Align switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, _ => TextAlignment.Left };
        Label.Effect = s.Shadow
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 0.95 }
            : null;
        Original.FontSize = 11;
        Original.Foreground = MakeBrush(s.TextColor, 72);
        Card.Background = MakeBrush(s.BackColor, s.BackOpacity);
        Card.BorderBrush = MakeBrush(s.BorderColor, s.Border ? 100 : 0);
        Card.BorderThickness = new Thickness(s.Border ? 1 : 0);
        Card.Effect = s.Shadow
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = 0.5 }
            : null;
    }

    public static System.Drawing.Rectangle DefaultPreviewRegion()
    {
        int w = GetSystemMetrics(SM_CXSCREEN);
        int h = GetSystemMetrics(SM_CYSCREEN);
        return new System.Drawing.Rectangle(w / 2 - 400, h * 3 / 4, 800, 120);
    }

    public void ShowPreview(System.Drawing.Rectangle region) =>
        ShowText("هذه معاينة الترجمة. سيظهر النص العربي هنا فوق اللعبة.",
                 "Translation preview", region, 7);

    public void ShowText(string text, string? original, System.Drawing.Rectangle regionPx, int hideAfter = -1)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _hideTimer.Stop();
        Label.Text = text;
        Original.Text = original ?? "";
        Original.Visibility = _style.ShowOriginal && !string.IsNullOrWhiteSpace(original)
            ? Visibility.Visible : Visibility.Collapsed;

        UpdateLayout();
        double sx = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double sy = VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (sx <= 0) sx = 1;
        if (sy <= 0) sy = sx;
        double screenWidth = GetSystemMetrics(SM_CXSCREEN) / sx;
        Left = Math.Max(8, Math.Min(screenWidth - ActualWidth - 8,
            (regionPx.X + regionPx.Width / 2.0) / sx - ActualWidth / 2));
        Top = Math.Max(8, regionPx.Y / sy - ActualHeight - 10);

        if (!IsVisible) Show();
        int seconds = hideAfter >= 0 ? hideAfter : _style.AutoHideSeconds;
        if (seconds > 0)
        {
            _hideTimer.Interval = TimeSpan.FromSeconds(seconds);
            _hideTimer.Start();
        }
    }

    public new void Hide()
    {
        _hideTimer.Stop();
        base.Hide();
    }
}

public sealed class WpfOverlaySink : IOverlaySink
{
    private readonly OverlayWindow _window;
    private readonly Func<System.Drawing.Rectangle> _region;

    public WpfOverlaySink(OverlayWindow window, Func<System.Drawing.Rectangle> region)
    {
        _window = window;
        _region = region;
    }

    public void Show(int regionId, string translated) => Show(regionId, translated, "");

    public void Show(int regionId, string translated, string source) =>
        _window.Dispatcher.InvokeAsync(() =>
        {
            try { _window.ShowText(translated, source, _region()); }
            catch (Exception ex) { AppLog.Write(ex); }
        });

    public void Clear(int regionId) => _window.Dispatcher.InvokeAsync(() => _window.Hide());
}
