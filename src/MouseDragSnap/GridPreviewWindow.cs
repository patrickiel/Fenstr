using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;
using static Fenstr.MouseDragSnapInterop;

namespace Fenstr;

/// <summary>
/// Per-monitor grid outline shown briefly when the wheel changes division
/// count during a drag. Holds for a moment, then fades out.
/// </summary>
internal sealed class GridPreviewWindow
{
    private const string ClassName = "FenstrGridPreview";
    private const uint OverlayColorBgr = 0x00FF7800u;
    private const byte FullAlpha = 0xC8;
    private const int BorderWidthPx = 3;
    private const uint HoldMs = 1500;
    private const uint FadeMs = 200;
    private const uint FadeTickMs = 30;
    private const nuint FadeTimerId = 1;

    private static readonly WndProcDelegate s_wndProc = WndProc;
    private static readonly TimerProcDelegate s_fadeProc = FadeTimerProc;
    private static readonly Dictionary<nint, long> s_showStart = [];
    private static bool s_classRegistered;
    private static nint s_brush;

    public nint Hwnd { get; }
    public string MonitorId { get; }

    private GridPreviewWindow(nint hwnd, string monitorId)
    {
        Hwnd = hwnd;
        MonitorId = monitorId;
    }

    public static GridPreviewWindow Create(string monitorId, RECT monitorBounds)
    {
        EnsureClassRegistered();

        var hInstance = GetModuleHandle(null);
        var hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            ClassName,
            string.Empty,
            WS_POPUP,
            monitorBounds.Left, monitorBounds.Top,
            monitorBounds.Width, monitorBounds.Height,
            0, 0, hInstance, 0);

        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

        SetLayeredWindowAttributes(hwnd, 0, FullAlpha, LWA_ALPHA);
        return new GridPreviewWindow(hwnd, monitorId);
    }

    public void Show(MonitorEntry monitor, IReadOnlyList<Region> regions)
    {
        SetLayeredWindowAttributes(Hwnd, 0, FullAlpha, LWA_ALPHA);

        var hrgn = BuildBorderRegion(monitor, regions);
        SetWindowRgn(Hwnd, hrgn, true);

        var wa = monitor.WorkArea;
        SetWindowPos(Hwnd, HWND_TOPMOST,
            wa.Left, wa.Top, wa.Width, wa.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        InvalidateRect(Hwnd, 0, true);

        s_showStart[Hwnd] = Environment.TickCount64;
        SetTimer(Hwnd, FadeTimerId, FadeTickMs, s_fadeProc);
    }

    public void Hide()
    {
        if (Hwnd == 0) return;
        KillTimer(Hwnd, FadeTimerId);
        s_showStart.Remove(Hwnd);
        ShowWindow(Hwnd, SW_HIDE);
        SetWindowRgn(Hwnd, 0, false);
        SetLayeredWindowAttributes(Hwnd, 0, FullAlpha, LWA_ALPHA);
    }

    public void Destroy()
    {
        s_showStart.Remove(Hwnd);
        if (Hwnd != 0) DestroyWindow(Hwnd);
    }

    private static nint BuildBorderRegion(MonitorEntry m, IReadOnlyList<Region> regions)
    {
        var wa = m.WorkArea;
        int w = wa.Width;
        int h = wa.Height;

        nint combined = CreateRectRgn(0, 0, 0, 0);
        AddStrip(combined, 0, 0, w, BorderWidthPx);
        AddStrip(combined, 0, h - BorderWidthPx, w, h);
        AddStrip(combined, 0, 0, BorderWidthPx, h);
        AddStrip(combined, w - BorderWidthPx, 0, w, h);

        int half = BorderWidthPx / 2;
        foreach (var r in regions)
        {
            if (r.MonitorId != m.Id) continue;

            if (m.IsLandscape)
            {
                int x = r.Rect.Right - wa.Left;
                if (x > 0 && x < w)
                    AddStrip(combined, x - half, 0, x - half + BorderWidthPx, h);
            }
            else
            {
                int y = r.Rect.Bottom - wa.Top;
                if (y > 0 && y < h)
                    AddStrip(combined, 0, y - half, w, y - half + BorderWidthPx);
            }
        }

        return combined;
    }

    private static void AddStrip(nint combined, int left, int top, int right, int bottom)
    {
        var strip = CreateRectRgn(left, top, right, bottom);
        CombineRgn(combined, combined, strip, RGN_OR);
        DeleteObject(strip);
    }

    private static void EnsureClassRegistered()
    {
        if (s_classRegistered) return;

        s_brush = CreateSolidBrush(OverlayColorBgr);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = s_wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = s_brush,
        };

        if (RegisterClassEx(ref wc) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");

        s_classRegistered = true;
    }

    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
        => DefWindowProc(hwnd, msg, wParam, lParam);

    private static void FadeTimerProc(nint hwnd, uint msg, nuint id, uint time)
    {
        if (!s_showStart.TryGetValue(hwnd, out long start))
        {
            KillTimer(hwnd, id);
            return;
        }

        long elapsed = Environment.TickCount64 - start;

        if (elapsed < HoldMs) return;

        long fadeElapsed = elapsed - HoldMs;
        if (fadeElapsed >= FadeMs)
        {
            KillTimer(hwnd, id);
            ShowWindow(hwnd, SW_HIDE);
            SetWindowRgn(hwnd, 0, false);
            SetLayeredWindowAttributes(hwnd, 0, FullAlpha, LWA_ALPHA);
            s_showStart.Remove(hwnd);
            return;
        }

        byte alpha = (byte)(FullAlpha * (1.0 - (double)fadeElapsed / FadeMs));
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
    }
}
