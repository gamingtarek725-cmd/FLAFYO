using System.Runtime.InteropServices;

namespace Flafyo.Windows;

public sealed record WindowRestoreInfo(IntPtr Hwnd, uint Style, uint ExStyle, System.Drawing.Rectangle Bounds);

/// <summary>
/// Turns another program's window into a borderless window that covers its whole screen ("fake fullscreen").
/// Only normal window settings are changed from outside; nothing is injected into the game.
/// </summary>
public static class WindowTools
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20, SW_RESTORE = 9;
    private const uint SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, MONITOR_DEFAULTTONEAREST = 2;
    private const uint WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000,
                       WS_MAXIMIZEBOX = 0x00010000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    private const uint WS_EX_DLGMODALFRAME = 0x1, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000;

    private static uint Get(IntPtr h, int index) => unchecked((uint)GetWindowLongPtr(h, index).ToInt64());
    private static void Set(IntPtr h, int index, uint value) => SetWindowLongPtr(h, index, new IntPtr(unchecked((int)value)));

    /// <summary>Removes the borders and stretches the window over the whole screen it is on. Returns what is needed to undo it.</summary>
    public static WindowRestoreInfo? MakeBorderless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !GetWindowRect(hwnd, out var r)) return null;
        uint style = Get(hwnd, GWL_STYLE), ex = Get(hwnd, GWL_EXSTYLE);
        var info = new WindowRestoreInfo(hwnd, style, ex, new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi)) return null;
        var m = mi.rcMonitor;

        if (IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        Set(hwnd, GWL_STYLE, (style & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP | WS_VISIBLE);
        Set(hwnd, GWL_EXSTYLE, ex & ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE));
        SetWindowPos(hwnd, IntPtr.Zero, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        return info;
    }

    public static bool Restore(WindowRestoreInfo i)
    {
        if (!IsWindow(i.Hwnd)) return false;
        Set(i.Hwnd, GWL_STYLE, i.Style);
        Set(i.Hwnd, GWL_EXSTYLE, i.ExStyle);
        SetWindowPos(i.Hwnd, IntPtr.Zero, i.Bounds.X, i.Bounds.Y, i.Bounds.Width, i.Bounds.Height, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        return true;
    }
}
