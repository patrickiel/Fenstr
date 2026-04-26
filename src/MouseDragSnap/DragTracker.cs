using System.Diagnostics;
using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;
using static Fenstr.MouseDragSnapInterop;

namespace Fenstr;

internal static class DragTracker
{
    private static readonly WinEventDelegate s_winEventProc = WinEventProc;
    private static readonly TimerProcDelegate s_timerProc = TimerProc;
    private static readonly LowLevelMouseProc s_mouseProc = MouseHookProc;

    private static nint s_hook;
    private static nint s_mouseHook;
    private static nuint s_timerId;
    private static DragSession s_session = null!;

    private static nint s_pendingMaxHwnd;
    private static POINT s_pendingMaxStart;

    private static bool s_manualDrag;
    private static int s_manualDragOffsetX;
    private static int s_manualDragOffsetY;

    private static readonly int s_dragThresholdX = GetSystemMetrics(SM_CXDRAG);
    private static readonly int s_dragThresholdY = GetSystemMetrics(SM_CYDRAG);
    private static readonly int s_captionZone = GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER) + GetSystemMetrics(SM_CYCAPTION) * 2;
    private static readonly uint s_wpSize = (uint)Marshal.SizeOf<WINDOWPLACEMENT>();

    public static bool IsDragActive => s_session.IsDragActive;

    public static void Start(
        List<Region> regions,
        List<MonitorEntry> monitors,
        Dictionary<string, OverlayWindow> overlays,
        Dictionary<string, GridPreviewWindow> gridPreviews,
        Config config)
    {
        s_session = new DragSession(regions, monitors, overlays, gridPreviews, config);

        s_hook = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
            0, s_winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        InstallMouseHook();
    }

    public static void Stop()
    {
        if (s_timerId != 0)
        {
            KillTimer(0, s_timerId);
            s_timerId = 0;
        }
        UninstallMouseHook();
        if (s_hook != 0)
        {
            UnhookWinEvent(s_hook);
            s_hook = 0;
        }
        s_pendingMaxHwnd = 0;
        s_manualDrag = false;
        s_session.HideActiveOverlay();
        s_session.ResetDragState();
    }

    public static void RefreshActiveOverlayLabel() => s_session.RefreshActiveOverlayLabel();

    public static void UpdateConfig(Config config) => s_session.UpdateConfig(config);

    public static void OnShiftDown()
    {
        if (!s_session.IsDragActive) return;
        if (GetCursorPos(out var pt)) s_session.OnShiftDown(pt);
    }

    public static void OnShiftUp()
    {
        if (!s_session.IsDragActive) return;
        if (GetCursorPos(out var pt)) s_session.OnShiftUp(pt);
    }

    private static void WinEventProc(nint hook, uint eventType, nint hwnd, int idObj, int idChild, uint thread, uint time)
    {
        if (idObj != 0) return;

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            Debug.WriteLine($"[DragTracker] MOVESIZESTART hwnd=0x{hwnd:X}");
            s_session.BeginDrag(hwnd);
            if (s_timerId == 0)
                s_timerId = SetTimer(0, 0, 30, s_timerProc);
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            Debug.WriteLine($"[DragTracker] MOVESIZEEND hwnd=0x{hwnd:X}");
            if (s_timerId != 0)
            {
                KillTimer(0, s_timerId);
                s_timerId = 0;
            }
            if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
                s_session.SwallowRmbUp = true;
            s_session.OnDragEnd();
        }
    }

    private static void InstallMouseHook()
    {
        if (s_mouseHook != 0) return;
        s_mouseHook = SetWindowsHookEx(WH_MOUSE_LL, s_mouseProc, GetModuleHandle(null), 0);
    }

    private static void UninstallMouseHook()
    {
        if (s_mouseHook == 0) return;
        UnhookWindowsHookEx(s_mouseHook);
        s_mouseHook = 0;
    }

    private static nint MouseHookProc(int nCode, nuint wParam, nint lParam)
    {
        if (nCode != HC_ACTION)
            return CallNextHookEx(0, nCode, wParam, lParam);

        int msg = (int)wParam;

        if (s_manualDrag)
        {
            if (msg == WM_MOUSEMOVE)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int newX = data.pt.X - s_manualDragOffsetX;
                int newY = data.pt.Y - s_manualDragOffsetY;
                SetWindowPos(s_session.DraggedHwnd, 0, newX, newY, 0, 0,
                    SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
                return CallNextHookEx(0, nCode, wParam, lParam);
            }

            if (msg == WM_LBUTTONUP)
            {
                Debug.WriteLine("[DragTracker] manual drag END (LBUTTONUP)");
                s_manualDrag = false;
                if (s_timerId != 0)
                {
                    KillTimer(0, s_timerId);
                    s_timerId = 0;
                }
                if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
                    s_session.SwallowRmbUp = true;
                s_session.OnDragEnd();
                return CallNextHookEx(0, nCode, wParam, lParam);
            }
        }

        if (msg == WM_LBUTTONDOWN && s_pendingMaxHwnd == 0 && !s_session.IsDragActive && !s_manualDrag)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((data.flags & LLMHF_INJECTED) == 0)
            {
                nint hit = WindowFromPoint(data.pt);
                nint hwnd = GetAncestor(hit, GA_ROOT);
                bool zoomed = hwnd != 0 && IsZoomed(hwnd);
                bool caption = zoomed && IsOnCaption(hwnd, data.pt);
                Debug.WriteLine($"[DragTracker] LBUTTONDOWN pt=({data.pt.X},{data.pt.Y}) hitChild=0x{hit:X} root=0x{hwnd:X} zoomed={zoomed} caption={caption}");
                if (caption)
                {
                    s_pendingMaxHwnd = hwnd;
                    s_pendingMaxStart = data.pt;
                    Debug.WriteLine($"[DragTracker] pendingMax SET hwnd=0x{hwnd:X} -- click passes through");
                }
            }
        }

        if (s_pendingMaxHwnd != 0)
        {
            if (msg == WM_MOUSEMOVE)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int dx = Math.Abs(data.pt.X - s_pendingMaxStart.X);
                int dy = Math.Abs(data.pt.Y - s_pendingMaxStart.Y);
                if (dx > s_dragThresholdX || dy > s_dragThresholdY)
                {
                    nint hwnd = s_pendingMaxHwnd;
                    POINT start = s_pendingMaxStart;
                    s_pendingMaxHwnd = 0;
                    Debug.WriteLine($"[DragTracker] drag threshold exceeded dx={dx} dy={dy} -- restoring and starting manual drag for 0x{hwnd:X}");
                    BeginManualDrag(hwnd, start, data.pt);
                }
                return CallNextHookEx(0, nCode, wParam, lParam);
            }

            if (msg == WM_LBUTTONUP)
            {
                Debug.WriteLine($"[DragTracker] LBUTTONUP while pending -- cancelling pendingMax 0x{s_pendingMaxHwnd:X}");
                s_pendingMaxHwnd = 0;
            }
        }

        if (msg == WM_RBUTTONUP)
        {
            if (s_session.DraggedHwnd != 0)
            {
                s_session.SwallowRmbUp = false;
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                s_session.OnRmbUp(data.pt);
                return 1;
            }
            if (s_session.SwallowRmbUp)
            {
                s_session.SwallowRmbUp = false;
                return 1;
            }
        }
        if (msg == WM_MOUSEWHEEL && s_session.ActiveRegion != null)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            short delta = (short)((data.mouseData >> 16) & 0xFFFF);
            s_session.OnMouseWheel(data.pt, delta);
            return 1;
        }
        if (msg == WM_RBUTTONDOWN && s_session.DraggedHwnd != 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            s_session.OnRmbDown(data.pt);
            return 1;
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static void BeginManualDrag(nint hwnd, POINT clickPt, POINT currentPt)
    {
        if (!IsZoomed(hwnd))
        {
            Debug.WriteLine("[DragTracker] BeginManualDrag BAIL: not zoomed");
            return;
        }
        if (!GetWindowRect(hwnd, out var maxRect))
        {
            Debug.WriteLine("[DragTracker] BeginManualDrag BAIL: GetWindowRect failed");
            return;
        }

        double xRatio = maxRect.Width > 0
            ? (double)(clickPt.X - maxRect.Left) / maxRect.Width
            : 0.5;
        int yOff = clickPt.Y - maxRect.Top;

        var wp = new WINDOWPLACEMENT { length = s_wpSize };
        GetWindowPlacement(hwnd, ref wp);
        int restoredW = wp.rcNormalPosition.Width;
        int restoredH = wp.rcNormalPosition.Height;

        ShowWindow(hwnd, SW_RESTORE);

        int newLeft = currentPt.X - (int)(xRatio * restoredW);
        int newTop = currentPt.Y - yOff;
        SetWindowPos(hwnd, 0, newLeft, newTop, restoredW, restoredH,
            SWP_NOACTIVATE | SWP_NOZORDER);

        s_manualDragOffsetX = currentPt.X - newLeft;
        s_manualDragOffsetY = currentPt.Y - newTop;
        s_manualDrag = true;
        Debug.WriteLine($"[DragTracker] BeginManualDrag: restored to ({newLeft},{newTop}) size=({restoredW}x{restoredH}) offset=({s_manualDragOffsetX},{s_manualDragOffsetY})");

        s_session.BeginDrag(hwnd);
        if (s_timerId == 0)
            s_timerId = SetTimer(0, 0, 30, s_timerProc);
    }

    private static bool IsOnCaption(nint hwnd, POINT pt)
    {
        nint lp = (nint)((pt.Y << 16) | (pt.X & 0xFFFF));
        if (SendMessageTimeout(hwnd, WM_NCHITTEST, 0, lp,
                SMTO_ABORTIFHUNG, 20, out nint result) == 0)
        {
            Debug.WriteLine($"[DragTracker] IsOnCaption hwnd=0x{hwnd:X} SendMessageTimeout FAILED");
            return false;
        }

        int hitCode = (int)(result & 0xFFFF);
        if (hitCode == HTCAPTION)
        {
            Debug.WriteLine($"[DragTracker] IsOnCaption hwnd=0x{hwnd:X} pt=({pt.X},{pt.Y}) hitTest=HTCAPTION -> true");
            return true;
        }

        if (hitCode == HTCLIENT && GetWindowRect(hwnd, out var rect))
        {
            bool inCaptionZone = pt.Y < rect.Top + s_captionZone;
            Debug.WriteLine($"[DragTracker] IsOnCaption hwnd=0x{hwnd:X} pt=({pt.X},{pt.Y}) hitTest=HTCLIENT rectTop={rect.Top} captionZone={s_captionZone} threshold={rect.Top + s_captionZone} -> {inCaptionZone}");
            return inCaptionZone;
        }

        Debug.WriteLine($"[DragTracker] IsOnCaption hwnd=0x{hwnd:X} pt=({pt.X},{pt.Y}) hitTest={hitCode} -> false");
        return false;
    }

    private static void TimerProc(nint hwnd, uint msg, nuint id, uint time)
    {
        if (!GetCursorPos(out var pt)) return;

        var hit = s_session.ResolveTarget(pt);
        if (RegionHitTester.SameTarget(hit, s_session.ActiveRegion)) return;

        s_session.HideActiveOverlay();
        s_session.SetActiveRegion(hit);
    }
}
