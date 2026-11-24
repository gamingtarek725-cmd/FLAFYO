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
        double vx = GetSystemMetrics(SM_XVIRTUALSCREEN) / scale, vy = GetSystemMetrics(SM_YVIRTUALSCREEN) / scale;
        double vw = GetSystemMetrics(SM_CXVIRTUALSCREEN) / scale, vh = GetSystemMetrics(SM_CYVIRTUALSCREEN) / scale;
        Left = vx; Top = vy; Width = vw; Height = vh;

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
            double x = (area.X + blocks[i].Block.Box.X) / scale - vx;
            double y = (area.Y + blocks[i].Block.Box.Y) / scale - vy;
            Canvas.SetLeft(card, Math.Max(0, Math.Min(x, vw - card.Width)));
            Canvas.SetTop(card, Math.Max(0, Math.Min(y, vh - card.DesiredSize.Height)));
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
