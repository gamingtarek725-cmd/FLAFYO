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

    public ScreenOverlayWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.Apply(hwnd);     // click-through, never takes focus, hidden from screen capture (so OCR never reads our own cards)
    }

    public void ShowBlocks(IReadOnlyList<TranslatedBlock> blocks, System.Drawing.Rectangle area, OverlayStyle style)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        double vx = GetSystemMetrics(SM_XVIRTUALSCREEN) / scale, vy = GetSystemMetrics(SM_YVIRTUALSCREEN) / scale;
        double vw = GetSystemMetrics(SM_CXVIRTUALSCREEN) / scale, vh = GetSystemMetrics(SM_CYVIRTUALSCREEN) / scale;
        Left = vx; Top = vy; Width = vw; Height = vh;

        Layer.Children.Clear();
        foreach (var tb in blocks)
        {
            var card = MakeCard(tb, scale, style, vw);
            double x = (area.X + tb.Block.Box.X) / scale - vx;
            double y = (area.Y + tb.Block.Box.Y) / scale - vy;
            Canvas.SetLeft(card, Math.Max(0, Math.Min(x, vw - card.Width)));
            Canvas.SetTop(card, Math.Max(0, Math.Min(y, vh - card.DesiredSize.Height)));
            Layer.Children.Add(card);
        }
        if (!IsVisible) Show();
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

    public void ClearAll() { Layer.Children.Clear(); Hide(); }

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
