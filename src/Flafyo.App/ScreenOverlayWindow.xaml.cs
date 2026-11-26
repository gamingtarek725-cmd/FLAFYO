using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

/// <summary>A click-through layer over the whole screen; every translated text block gets its own card on top of the original.</summary>
public partial class ScreenOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private readonly List<Border> _cards = new();

    public ScreenOverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd);     // click-through and visible to OBS/display capture; OCR filters Arabic overlay text.
        Opacity = 0; // Resolve the real monitor DPI after Show(), before placing cards in physical screen coordinates.
    }

    public void ShowBlocks(IReadOnlyList<TranslatedBlock> blocks, System.Drawing.Rectangle area, OverlayStyle style)
    {
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        UpdateLayout();

        // WPF can report the default 96-DPI scale for a handle that has not been shown yet.
        // Read the scale only after the window is visible on its target display.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        int vxPx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vyPx = GetSystemMetrics(SM_YVIRTUALSCREEN);
        double vw = GetSystemMetrics(SM_CXVIRTUALSCREEN) / scale;
        double vh = GetSystemMetrics(SM_CYVIRTUALSCREEN) / scale;
        Left = vxPx / scale;
        Top = vyPx / scale;
        Width = vw;
        Height = vh;
        UpdateLayout();

        for (int i = 0; i < blocks.Count; i++)
        {
            var fresh = MakeCard(blocks[i], scale, style, vw);
            Border card;
            if (i >= _cards.Count)
            {
                card = fresh;
                _cards.Add(card);
                Layer.Children.Add(card);
            }
            else
            {
                card = _cards[i];
                CopyCard(card, fresh);
            }

            card.Visibility = Visibility.Visible;
            card.Measure(new Size(card.Width, double.PositiveInfinity));
            // Convert physical desktop pixels through WPF's actual visual transform, not a guessed DPI divisor.
            var screenPoint = new Point(
                area.X + blocks[i].Block.Box.X,
                area.Y + blocks[i].Block.Box.Y);
            var localPoint = Layer.PointFromScreen(screenPoint);
            Canvas.SetLeft(card, Math.Max(0, Math.Min(localPoint.X, vw - card.Width)));
            Canvas.SetTop(card, Math.Max(0, Math.Min(localPoint.Y, vh - card.DesiredSize.Height)));
        }

        // Keep existing card objects but hide those no longer present, avoiding needless visual-tree rebuilds.
        for (int i = blocks.Count; i < _cards.Count; i++)
            _cards[i].Visibility = Visibility.Collapsed;

        Opacity = 1;
    }

    private static void CopyCard(Border target, Border source)
    {
        target.Child = source.Child;
        target.Width = source.Width;
        target.Padding = source.Padding;
        target.CornerRadius = source.CornerRadius;
        target.SnapsToDevicePixels = source.SnapsToDevicePixels;
        target.Effect = source.Effect;
        target.FlowDirection = source.FlowDirection;
        target.Background = source.Background;
        target.BorderBrush = source.BorderBrush;
        target.BorderThickness = source.BorderThickness;
    }

    private static Border MakeCard(TranslatedBlock tb, double scale, OverlayStyle s, double maxWidth)
    {
        bool heading = tb.Block.Kind == BlockKind.Heading;
        double lineH = tb.Block.LineHeight / scale;
        double font = s.AutoFit
            ? Math.Clamp(lineH * 0.72, 10, 64)
            : Math.Clamp(s.FontSize * (heading ? s.HeadingScalePercent / 100.0 : 1.0), 10, 96);

        FontFamily family;
        try { family = new FontFamily(s.FontFamily); } catch { family = new FontFamily("Segoe UI"); }

        var text = new TextBlock
        {
            Text = tb.Arabic,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = family,
            FontSize = font,
            FontWeight = (heading ? s.HeadingBold : s.Bold) ? FontWeights.Bold : FontWeights.Normal,
            Foreground = OverlayWindow.MakeBrush(heading ? s.HeadingTextColor : s.TextColor),
            TextAlignment = s.Align switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, _ => TextAlignment.Left },
            Effect = s.Shadow ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1.2, Opacity = 0.9 } : null,
        };
        double width = Math.Min(Math.Max(tb.Block.Box.Width / scale + 14, 70), maxWidth);
        var card = new Border
        {
            Child = text,
            Width = width,
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(8),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.32 },
            FlowDirection = FlowDirection.RightToLeft,
            Background = OverlayWindow.MakeBrush(s.BackColor, s.BackOpacity),
            BorderBrush = OverlayWindow.MakeBrush(heading ? s.HeadingTextColor : s.BorderColor),
            BorderThickness = new Thickness(s.Border ? 1 : 0),
        };

        // shrink the text until it fits the place of the original text
        double allowed = Math.Max(tb.Block.Box.Height / scale * 1.5, lineH * 1.9);
        card.Measure(new Size(width, double.PositiveInfinity));
        while (card.DesiredSize.Height > allowed && text.FontSize > 10)
        {
            text.FontSize -= 1;
            card.Measure(new Size(width, double.PositiveInfinity));
        }
        return card;
    }

    public void ClearAll()
    {
        Layer.Children.Clear();
        _cards.Clear();
        Hide();
    }

    public void ToggleVisible() { if (IsVisible) Hide(); else if (Layer.Children.Count > 0) Show(); }
}

public sealed class WpfScreenOverlay : IScreenOverlay
{
    private readonly ScreenOverlayWindow _window;
    private readonly Func<OverlayStyle> _style;

    public WpfScreenOverlay(ScreenOverlayWindow window, Func<OverlayStyle> style) { _window = window; _style = style; }

    public void Show(IReadOnlyList<TranslatedBlock> blocks, System.Drawing.Rectangle area) =>
        _window.Dispatcher.InvokeAsync(() =>
        {
            try { _window.ShowBlocks(blocks, area, _style()); } catch (Exception ex) { AppLog.Write(ex); }
        });

    public void Clear() => _window.Dispatcher.InvokeAsync(() => _window.ClearAll());
}

/// <summary>
/// Region-mode translation sink. Translation cards are rendered in the independent
/// click-through screen layer; FLAFYO Island remains a status/control surface only.
/// </summary>
public sealed class WpfRegionOverlaySink : IOverlaySink
{
    private readonly ScreenOverlayWindow _window;
    private readonly Func<System.Drawing.Rectangle> _region;
    private readonly Func<OverlayStyle> _style;

    public WpfRegionOverlaySink(
        ScreenOverlayWindow window,
        Func<System.Drawing.Rectangle> region,
        Func<OverlayStyle> style)
    {
        _window = window;
        _region = region;
        _style = style;
    }

    public void Show(int regionId, string translated) => Show(regionId, translated, "");

    public void Show(int regionId, string translated, string source)
    {
        var area = _region();
        if (area.Width < 20 || area.Height < 10 || string.IsNullOrWhiteSpace(translated))
            return;

        var box = new System.Drawing.Rectangle(
            area.Width / 10,
            area.Height / 4,
            Math.Max(1, area.Width * 8 / 10),
            Math.Max(1, area.Height / 2));
        var style = _style();
        var lineHeight = Math.Max(16, style.FontSize);
        var block = new ScreenBlock(source ?? "", box, BlockKind.Body, lineHeight);
        var translatedBlocks = new[] { new TranslatedBlock(block, translated) };

        _window.Dispatcher.InvokeAsync(() =>
        {
            try { _window.ShowBlocks(translatedBlocks, area, _style()); }
            catch (Exception ex) { AppLog.Write(ex); }
        });
    }

    public void Clear(int regionId) =>
        _window.Dispatcher.InvokeAsync(() => _window.ClearAll());
}

