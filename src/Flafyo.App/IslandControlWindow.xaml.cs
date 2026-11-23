using System;
using System.Windows;
using System.Windows.Interop;
using Flafyo.Windows;

namespace Flafyo.Desktop;

/// <summary>A tiny non-activating HWND above the Island. Only this button receives mouse input.</summary>
public partial class IslandControlWindow : Window
{
    public event Action? ToggleRequested;

    public IslandControlWindow()
    {
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindowStyles.ApplyInteractive(hwnd);
    }

    public void SetRunning(bool running)
    {
        ToggleButton.Content = running ? "إيقاف" : "تشغيل";
        ToggleButton.Background = OverlayWindow.MakeBrush(running ? "#163D35" : "#18365A");
        ToggleButton.BorderBrush = OverlayWindow.MakeBrush(running ? "#347A66" : "#3D6FA8");
        ToggleButton.Foreground = OverlayWindow.MakeBrush(running ? "#D9FFF1" : "#DCEBFF");
        ToggleButton.ToolTip = running ? "إيقاف الترجمة" : "تشغيل الترجمة";
    }

    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        if (!IsVisible) Show();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => ToggleRequested?.Invoke();
}