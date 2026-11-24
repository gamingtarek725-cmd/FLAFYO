using System.Runtime.InteropServices;

namespace Flafyo.Windows;

/// <summary>Makes an overlay click-through, non-activating, always-on-top, and visible to screen capture tools.</summary>
public static class OverlayWindowStyles
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080,
                       WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const uint WDA_NONE = 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    /// <summary>Applies a fully click-through overlay style.</summary>
    public static void Apply(IntPtr hwnd) => ApplyCore(hwnd, clickThrough: true);

    /// <summary>Applies a non-activating style for the small, dedicated Island control window.</summary>
    public static void ApplyInteractive(IntPtr hwnd) => ApplyCore(hwnd, clickThrough: false);

    private static void ApplyCore(IntPtr hwnd, bool clickThrough)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        if (clickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        // WDA_NONE keeps the overlay visible to OBS and normal desktop capture applications.
        // The screen OCR pipeline filters the overlay's Arabic output and the app's own brand text.
        SetWindowDisplayAffinity(hwnd, WDA_NONE);
    }
}
