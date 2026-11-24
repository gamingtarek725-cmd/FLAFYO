using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Flafyo.Windows;

namespace Flafyo.Desktop;

/// <summary>Minimal FLAFYO Island: translation state plus one start/stop control.</summary>
public partial class IslandStatusWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SM_CXSCREEN = 0;
    private readonly IslandControlWindow _controlWindow;

    public event Action? ToggleRequested;

    public IslandStatusWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.ApplyCaptureVisible(hwnd);
        _controlWindow = new IslandControlWindow();
        _controlWindow.Owner = this;
        _controlWindow.ToggleRequested += () => ToggleRequested?.Invoke();
        SizeChanged += (_, _) => PositionControlWindow();
        LocationChanged += (_, _) => PositionControlWindow();
        SetRunning(false);
    }

    public void SetRunning(bool running)
    {
        IslandCaption.Text = running ? "الترجمة تعمل" : "الترجمة متوقفة";
        IslandStatus.Text = running ? "ON" : "OFF";
        IslandStatus.Foreground = OverlayWindow.MakeBrush(running ? "#8CF2C5" : "#F4B860");
        IslandStatusDot.Fill = OverlayWindow.MakeBrush(running ? "#45E0A0" : "#F4B860");
        _controlWindow.SetRunning(running);
    }

    public void ShowIsland()
    {
        PositionAtTopCenter();
        if (!IsVisible) Show();
        PositionControlWindow();
    }

    private void PositionAtTopCenter()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        double screenWidth = GetSystemMetrics(SM_CXSCREEN) / scale;
        Left = Math.Max(8, (screenWidth - ActualWidth) / 2);
        Top = 18;
    }

    private void PositionControlWindow()
    {
        if (!IsVisible) { _controlWindow.Hide(); return; }
        try
        {
            var point = ToggleSlot.PointToScreen(new Point(0, 0));
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (scale <= 0) scale = 1;
            _controlWindow.ShowAt(point.X / scale + 3, point.Y / scale + 2);
        }
        catch { }
    }

    public new void Hide()
    {
        _controlWindow.Hide();
        base.Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _controlWindow.Close();
        base.OnClosed(e);
    }
}
