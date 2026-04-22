using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;
using static Fenstr.KeyboardShortcutsInterop;

namespace Fenstr;

/// <summary>
/// Low-level keyboard hook. Handles Win+arrow navigation, the
/// placement-mode hotkey (press assigned letters to snap; multi-press
/// unions regions on the same monitor), and assigning a region's hotkey by
/// typing while a drag overlay is active.
/// </summary>
internal static class KeyboardHook
{
    private static readonly LowLevelKeyboardProc s_proc = HookProc;
    private static nint s_hook;
    private static List<Region> s_regions = [];
    private static List<MonitorEntry> s_monitors = [];
    private static Config s_config = new([]);
    private static Dictionary<string, KeyboardSelectionWindow> s_selectionWindows = [];
    private static readonly HashSet<int> s_placementPressedVks = [];
    private static string s_placementMonitorId = string.Empty;

    public static void Start(
        List<Region> regions,
        List<MonitorEntry> monitors,
        Config config,
        Dictionary<string, KeyboardSelectionWindow> selectionWindows)
    {
        s_regions = regions;
        s_monitors = monitors;
        s_config = config;
        s_selectionWindows = selectionWindows;
        s_hook = SetWindowsHookEx(WH_KEYBOARD_LL, s_proc, GetModuleHandle(null), 0);
    }

    public static void Stop()
    {
        if (s_hook == 0) return;
        UnhookWindowsHookEx(s_hook);
        s_hook = 0;
    }

    public static void UpdateConfig(Config config)
    {
        s_config = config;
        if (!config.PlacementHotkeyEnabled && ModeState.PlacementModeActive)
            ExitPlacementMode();
    }

    private static nint HookProc(int nCode, nuint wParam, nint lParam)
    {
        if (nCode != HC_ACTION)
            return CallNextHookEx(0, nCode, wParam, lParam);

        int msg = (int)wParam;
        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
        if (!isDown && !isUp)
            return CallNextHookEx(0, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vk = (int)data.vkCode;

        if (vk == VK_SHIFT || vk == 0xA0 || vk == 0xA1)
        {
            if (DragTracker.IsDragActive)
            {
                if (isDown) DragTracker.OnShiftDown();
                else        DragTracker.OnShiftUp();
            }
            return CallNextHookEx(0, nCode, wParam, lParam);
        }

        if (ModeState.PlacementModeActive)
            return HandlePlacementMode(nCode, wParam, lParam, vk, isDown);

        if (!isDown)
            return CallNextHookEx(0, nCode, wParam, lParam);

        bool winDown = IsDown(VK_LWIN) || IsDown(VK_RWIN);
        bool ctrlDown = IsDown(VK_CONTROL);
        bool altDown = IsDown(VK_MENU);
        bool shiftDown = IsDown(VK_SHIFT);

        if (s_config.PlacementHotkeyEnabled
            && MatchesPlacementHotkey(vk, winDown, ctrlDown, altDown, shiftDown))
        {
            if (!DragTracker.IsDragActive)
                EnterPlacementMode();
            return 1;
        }

        if (ModeState.DragOverlayRegion is Region dragRegion)
        {
            if (vk == VK_DELETE)
            {
                s_config.RemoveHotkey(dragRegion.MonitorId, dragRegion.Index);
                s_config.Save();
                DragTracker.RefreshActiveOverlayLabel();
                return 1;
            }
            if (ModeState.IsValidHotkeyVk(vk) && !IsAnyModifierDown())
            {
                s_config.SetHotkey(dragRegion.MonitorId, dragRegion.Index, vk);
                s_config.Save();
                DragTracker.RefreshActiveOverlayLabel();
                return 1;
            }
        }

        if (winDown && !ctrlDown && !altDown && !shiftDown)
        {
            switch (vk)
            {
                case VK_LEFT:  CycleRegion(-1); return 1;
                case VK_RIGHT: CycleRegion(+1); return 1;
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static nint HandlePlacementMode(int nCode, nuint wParam, nint lParam, int vk, bool isDown)
    {
        if (IsModifierVk(vk))
            return CallNextHookEx(0, nCode, wParam, lParam);

        if (isDown && vk == VK_ESCAPE)
        {
            ExitPlacementMode();
            return 1;
        }

        if (isDown && MatchesPlacementHotkey(vk,
                winDown: IsDown(VK_LWIN) || IsDown(VK_RWIN),
                ctrlDown: IsDown(VK_CONTROL),
                altDown: IsDown(VK_MENU),
                shiftDown: IsDown(VK_SHIFT)))
        {
            ExitPlacementMode();
            return 1;
        }

        if (isDown)
        {
            if (!ModeState.IsValidHotkeyVk(vk))
                return 1;

            var loc = s_config.FindRegionByHotkey(vk);
            if (loc is (string monitorId, int _))
            {
                if (s_placementPressedVks.Count == 0)
                    s_placementMonitorId = monitorId;

                if (monitorId == s_placementMonitorId)
                    s_placementPressedVks.Add(vk);
            }
            return 1;
        }

        if (s_placementPressedVks.Contains(vk))
        {
            SnapToPressedUnion();
            ExitPlacementMode();
            return 1;
        }
        return 1;
    }

    private static void SnapToPressedUnion()
    {
        if (ModeState.PlacementTargetHwnd == 0 || s_placementPressedVks.Count == 0) return;

        var rects = new List<RECT>();
        foreach (var v in s_placementPressedVks)
        {
            var loc = s_config.FindRegionByHotkey(v);
            if (loc == null) continue;
            var r = FindRegion(loc.Value.MonitorId, loc.Value.Index);
            if (r != null) rects.Add(r.Rect);
        }
        if (rects.Count == 0) return;

        var union = rects[0];
        for (int i = 1; i < rects.Count; i++)
        {
            if (rects[i].Left < union.Left) union.Left = rects[i].Left;
            if (rects[i].Top < union.Top) union.Top = rects[i].Top;
            if (rects[i].Right > union.Right) union.Right = rects[i].Right;
            if (rects[i].Bottom > union.Bottom) union.Bottom = rects[i].Bottom;
        }
        WindowSnapper.Snap(ModeState.PlacementTargetHwnd, union);
    }

    private static void EnterPlacementMode()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return;

        ModeState.PlacementTargetHwnd = hwnd;
        ModeState.PlacementModeActive = true;
        s_placementPressedVks.Clear();
        s_placementMonitorId = string.Empty;

        foreach (var m in s_monitors)
            if (s_selectionWindows.TryGetValue(m.Id, out var win))
                win.Show(m, s_regions, s_config);
    }

    private static void ExitPlacementMode()
    {
        ModeState.PlacementModeActive = false;
        ModeState.PlacementTargetHwnd = 0;
        s_placementPressedVks.Clear();
        s_placementMonitorId = string.Empty;
        foreach (var win in s_selectionWindows.Values)
            win.Hide();
    }

    private static Region? FindRegion(string monitorId, int regionIndex)
    {
        foreach (var r in s_regions)
            if (r.MonitorId == monitorId && r.Index == regionIndex) return r;
        return null;
    }

    private static bool IsModifierVk(int vk)
        => vk == VK_LWIN || vk == VK_RWIN || vk == VK_CONTROL || vk == VK_MENU || vk == VK_SHIFT
           || vk == 0xA0 || vk == 0xA1 // L/R Shift
           || vk == 0xA2 || vk == 0xA3 // L/R Control
           || vk == 0xA4 || vk == 0xA5; // L/R Alt

    private static bool IsAnyModifierDown()
        => IsDown(VK_LWIN) || IsDown(VK_RWIN) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_SHIFT);

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool MatchesPlacementHotkey(int vk, bool winDown, bool ctrlDown, bool altDown, bool shiftDown)
    {
        var hk = s_config.PlacementHotkeyOrDefault;
        return vk == hk.VkCode
            && winDown == hk.Win
            && ctrlDown == hk.Ctrl
            && altDown == hk.Alt
            && shiftDown == hk.Shift;
    }

    private static void CycleRegion(int delta)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return;

        var ordered = OrderedRegions();
        if (ordered.Count == 0) return;

        int cx = (rect.Left + rect.Right) / 2;
        int cy = (rect.Top + rect.Bottom) / 2;

        int current = -1;
        for (int i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i].Rect;
            if (cx >= r.Left && cx < r.Right && cy >= r.Top && cy < r.Bottom)
            {
                current = i;
                break;
            }
        }
        if (current < 0) current = delta > 0 ? -1 : ordered.Count;

        int n = ordered.Count;
        int next = ((current + delta) % n + n) % n;

        WindowSnapper.Snap(hwnd, ordered[next].Rect);
    }

    private static List<Region> OrderedRegions()
    {
        var monitorOrder = new Dictionary<string, int>();
        var sortedMonitors = new List<MonitorEntry>(s_monitors);
        sortedMonitors.Sort((a, b) =>
        {
            int c = a.WorkArea.Left.CompareTo(b.WorkArea.Left);
            return c != 0 ? c : a.WorkArea.Top.CompareTo(b.WorkArea.Top);
        });
        for (int i = 0; i < sortedMonitors.Count; i++)
            monitorOrder[sortedMonitors[i].Id] = i;

        var result = new List<Region>(s_regions);
        result.Sort((a, b) =>
        {
            int c = monitorOrder[a.MonitorId].CompareTo(monitorOrder[b.MonitorId]);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });
        return result;
    }
}
