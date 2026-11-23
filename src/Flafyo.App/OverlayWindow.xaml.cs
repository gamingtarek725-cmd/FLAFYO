using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _collapseTimer = new();
    private OverlayStyle _style = new();
    private bool _expanded;
    private const double ExpandedWidth = 540;
    private const double CollapsedWidth = 226;
    private const double TopInset = 18;

    public OverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd); // click-through + never takes focus + hidden from capture
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        _collapseTimer.Tick += (_, _) => Collapse();
        ApplyStyle(_style);
        SetCollapsedVisual();
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
        try { Label.FontFamily = new FontFamily(s.FontFamily); Original.FontFamily = Label.FontFamily; }
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
        Card.Background = MakeBrush("#090F1B", 96);
        Card.BorderBrush = MakeBrush("#526B8B", 78);
        Card.BorderThickness = new Thickness(1);
        Card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 5, Opacity = 0.55 };
    }

    public static System.Drawing.Rectangle DefaultPreviewRegion()
    {
        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
        return new System.Drawing.Rectangle(w / 2 - 400, h * 3 / 4, 800, 120);
    }

    public void ShowPreview(System.Drawing.Rectangle region) =>
        ShowText("هذه معاينة FLAFYO Island. ستظهر الترجمة هنا أثناء اللعب.",
                 "This is a preview of your live game translation.", region, 7);

    /// <summary>Displays a click-through Dynamic Island style translation at the top-center of the primary display.</summary>
    public void ShowText(string text, string? original, System.Drawing.Rectangle regionPx, int hideAfter = -1)
    {
        Label.Text = text;
        Original.Text = original ?? "";
        Original.Visibility = _style.ShowOriginal && !string.IsNullOrWhiteSpace(original)
            ? Visibility.Visible : Visibility.Collapsed;

        DetailsPanel.Visibility = Visibility.Visible;
        IslandCaption.Text = "ترجمة جديدة";
        IslandStatus.Text = "LIVE";
        _expanded = true;
        Card.Padding = new Thickness(18, 12, 18, 12);
        Width = ExpandedWidth;
        BeginAnimation(WidthProperty, null);
        UpdateLayout();
        PositionAtTopCenter(ExpandedWidth);

        if (!IsVisible) Show();
        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromSeconds(4.5);
        _collapseTimer.Start();

        _hideTimer.Stop();
        int seconds = hideAfter >= 0 ? hideAfter : _style.AutoHideSeconds;
        if (seconds > 0)
        {
            _hideTimer.Interval = TimeSpan.FromSeconds(seconds);
            _hideTimer.Start();
        }
    }

    private void PositionAtTopCenter(double width)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        double screenWidth = GetSystemMetrics(SM_CXSCREEN) / scale;
        Left = Math.Max(8, (screenWidth - width / scale) / 2);
        Top = TopInset;
    }

    private void Collapse()
    {
        _collapseTimer.Stop();
        if (!IsVisible || !_expanded) return;
        _expanded = false;
        DetailsPanel.Visibility = Visibility.Collapsed;
        IslandCaption.Text = "FLAFYO جاهز";
        IslandStatus.Text = "ON";
        Card.Padding = new Thickness(14, 9, 14, 9);
        BeginAnimation(WidthProperty, new DoubleAnimation(Width, CollapsedWidth,
            TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        PositionAtTopCenter(CollapsedWidth);
    }

    private void SetCollapsedVisual()
    {
        DetailsPanel.Visibility = Visibility.Collapsed;
        IslandCaption.Text = "FLAFYO جاهز";
        IslandStatus.Text = "ON";
        Width = CollapsedWidth;
        PositionAtTopCenter(CollapsedWidth);
    }

    public new void Hide()
    {
        _hideTimer.Stop();
        _collapseTimer.Stop();
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

    // InvokeAsync: the pipeline never waits for the UI thread.
    public void Show(int regionId, string translated) => Show(regionId, translated, "");

    public void Show(int regionId, string translated, string source) =>
        _window.Dispatcher.InvokeAsync(() =>
        {
            try { _window.ShowText(translated, source, _region()); }
            catch (Exception ex) { AppLog.Write(ex); }
        });

    public void Clear(int regionId) => _window.Dispatcher.InvokeAsync(() => _window.Hide());
}