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
            s_session.BeginDrag(hwnd);
            if (s_timerId == 0)
                s_timerId = SetTimer(0, 0, 30, s_timerProc);
            InstallMouseHook();
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            if (s_timerId != 0)
            {
                KillTimer(0, s_timerId);
                s_timerId = 0;
            }
            if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
                s_session.SwallowRmbUp = true;
            if (!s_session.SwallowRmbUp)
                UninstallMouseHook();
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
        if (nCode == HC_ACTION)
        {
            int msg = (int)wParam;
            if (msg == WM_RBUTTONUP)
            {
                s_session.SwallowRmbUp = false;
                if (s_session.DraggedHwnd != 0)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    s_session.OnRmbUp(data.pt);
                }
                else
                {
                    UninstallMouseHook();
                }
                return 1;
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
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
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
