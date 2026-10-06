using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Flafyo.Desktop;

/// <summary>Full-screen drag selector (primary monitor). Returns the region in physical screen pixels.</summary>
public partial class RegionSelectorWindow : Window
{
    private System.Windows.Point _start;
    private bool _dragging;

    public System.Drawing.Rectangle SelectedRegion { get; private set; }

    public RegionSelectorWindow(string? hint = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(hint)) Hint.Text = hint;
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Loaded += (_, _) => Activate();
        Root.MouseLeftButtonDown += OnDown;
        Root.MouseMove += OnMove;
        Root.MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void OnDown(object s, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root); _dragging = true;
        Box.Visibility = Visibility.Visible;
        Root.CaptureMouse();
    }

    private void OnMove(object s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Root);
        Canvas.SetLeft(Box, Math.Min(_start.X, p.X)); Canvas.SetTop(Box, Math.Min(_start.Y, p.Y));
        Box.Width = Math.Abs(p.X - _start.X); Box.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnUp(object s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; Root.ReleaseMouseCapture();
        var end = e.GetPosition(Root);
        var tl = Root.PointToScreen(new System.Windows.Point(Math.Min(_start.X, end.X), Math.Min(_start.Y, end.Y)));
        var br = Root.PointToScreen(new System.Windows.Point(Math.Max(_start.X, end.X), Math.Max(_start.Y, end.Y)));
        SelectedRegion = new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
        DialogResult = SelectedRegion.Width >= 20 && SelectedRegion.Height >= 10;
    }
}
