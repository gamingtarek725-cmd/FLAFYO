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
    private readonly IslandControlWindow _controlWindow;
    private bool _expanded;
    private bool _translationRunning;
    public event Action? ToggleRequested;
    private const double ExpandedWidth = 540;
    private const double CollapsedWidth = 292;
    private const double TopInset = 18;

    public OverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd); // Island surface stays click-through.
        _controlWindow = new IslandControlWindow(); // only the small button receives mouse input.
        _controlWindow.Owner = this;
        _controlWindow.ToggleRequested += () => ToggleRequested?.Invoke();
        SizeChanged += (_, _) => PositionControlWindow();
        LocationChanged += (_, _) => PositionControlWindow();
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
        _controlWindow.SetRunning(_translationRunning);
        if (!s.IslandEnabled) Hide();
        else if (IsVisible) PositionControlWindow();
        if (!s.IslandShowToggleButton) _controlWindow.Hide();
        else if (IsVisible) PositionControlWindow();
    }

    public void SetTranslationRunning(bool running)
    {
        _translationRunning = running;
        _controlWindow.SetRunning(running);
        if (!_style.IslandEnabled) { Hide(); return; }
        if (!_expanded)
        {
            IslandCaption.Text = running ? "FLAFYO جاهز" : "الترجمة متوقفة";
            IslandStatus.Text = running ? "ON" : "PAUSED";
        }
    }

    public void ShowIdle()
    {
        if (!_style.IslandEnabled) { Hide(); return; }
        _hideTimer.Stop();
        _collapseTimer.Stop();
        _expanded = false;
        DetailsPanel.Visibility = Visibility.Collapsed;
        IslandCaption.Text = _translationRunning ? "FLAFYO جاهز" : "الترجمة متوقفة";
        IslandStatus.Text = _translationRunning ? "ON" : "PAUSED";
        Card.Padding = new Thickness(14, 9, 14, 9);
        Width = CollapsedWidth;
        BeginAnimation(WidthProperty, null);
        BeginAnimation(LeftProperty, null);
        PositionAtTopCenter(CollapsedWidth);
        if (!IsVisible) Show();
        PositionControlWindow();
    }

    private void PositionControlWindow()
    {
        if (!IsVisible || !_style.IslandEnabled || !_style.IslandShowToggleButton)
        {
            _controlWindow.Hide();
            return;
        }
        try
        {
            var point = IslandToggleSlot.PointToScreen(new Point(0, 0));
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (scale <= 0) scale = 1;
            _controlWindow.ShowAt(point.X / scale + 3, point.Y / scale + 1);
        }
        catch { }
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
        if (!_style.IslandEnabled) return;
        _hideTimer.Stop();
        Label.Text = text;
        Original.Text = original ?? "";
        Original.Visibility = _style.ShowOriginal && !string.IsNullOrWhiteSpace(original)
            ? Visibility.Visible : Visibility.Collapsed;

        DetailsPanel.Visibility = Visibility.Visible;
        IslandCaption.Text = "ترجمة جديدة";
        IslandStatus.Text = _translationRunning ? "LIVE" : "PREVIEW";
        _expanded = true;
        Card.Padding = new Thickness(18, 12, 18, 12);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(LeftProperty, null);
        Width = ExpandedWidth;
        UpdateLayout();
        PositionAtTopCenter(ExpandedWidth);

        if (!IsVisible) Show();
        PositionControlWindow();
        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_style.IslandCollapseSeconds, 1, 15));
        _collapseTimer.Start();

        _hideTimer.Stop();
        int seconds = hideAfter >= 0 ? hideAfter : _style.AutoHideSeconds;
        if (seconds > 0)
        {
            _hideTimer.Interval = TimeSpan.FromSeconds(seconds);
            _hideTimer.Start();
        }
    }

    private double CenteredLeft(double width)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        double screenWidth = GetSystemMetrics(SM_CXSCREEN) / scale;
        return Math.Max(8, (screenWidth - width) / 2);
    }

    private void PositionAtTopCenter(double width)
    {
        Left = CenteredLeft(width);
        Top = TopInset;
    }

    private void Collapse()
    {
        _collapseTimer.Stop();
        if (!IsVisible || !_expanded) return;
        _expanded = false;
        DetailsPanel.Visibility = Visibility.Collapsed;
        IslandCaption.Text = _translationRunning ? "FLAFYO جاهز" : "الترجمة متوقفة";
        IslandStatus.Text = _translationRunning ? "ON" : "PAUSED";
        Card.Padding = new Thickness(14, 9, 14, 9);
        var duration = TimeSpan.FromMilliseconds(260);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(WidthProperty, new DoubleAnimation(Width, CollapsedWidth, duration) { EasingFunction = easing });
        BeginAnimation(LeftProperty, new DoubleAnimation(Left, CenteredLeft(CollapsedWidth), duration) { EasingFunction = easing });
        PositionControlWindow();
    }

    private void SetCollapsedVisual()
    {
        DetailsPanel.Visibility = Visibility.Collapsed;
        IslandCaption.Text = _translationRunning ? "FLAFYO جاهز" : "الترجمة متوقفة";
        IslandStatus.Text = _translationRunning ? "ON" : "PAUSED";
        Width = CollapsedWidth;
        PositionAtTopCenter(CollapsedWidth);
    }

    public new void Hide()
    {
        _hideTimer.Stop();
        _collapseTimer.Stop();
        _controlWindow.Hide();
        base.Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _controlWindow.Close();
        base.OnClosed(e);
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

    public void Clear(int regionId) => _window.Dispatcher.InvokeAsync(() => _window.ShowIdle());
}