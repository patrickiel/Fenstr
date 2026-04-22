using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;

namespace Fenstr;

/// <summary>
/// Positions a window at a target rect, compensating for the DWM invisible
/// border so the visible frame lines up with the region edges.
/// </summary>
internal static class WindowSnapper
{
    public static void Snap(nint hwnd, RECT target)
    {
        if (hwnd == 0) return;

        if (IsZoomed(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        SetWindowPos(hwnd, 0, target.Left, target.Top, target.Width, target.Height,
            SWP_NOACTIVATE | SWP_NOZORDER);

        int rectSize = Marshal.SizeOf<RECT>();
        int dL = 0, dT = 0, dR = 0, dB = 0;

        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, rectSize) == 0
            && GetWindowRect(hwnd, out var outer))
        {
            dL = frame.Left - outer.Left;
            dT = frame.Top - outer.Top;
            dR = outer.Right - frame.Right;
            dB = outer.Bottom - frame.Bottom;
        }

        SetWindowPos(hwnd, 0,
            target.Left - dL,
            target.Top - dT,
            target.Width + dL + dR,
            target.Height + dT + dB,
            SWP_NOACTIVATE | SWP_NOZORDER);
    }
}
