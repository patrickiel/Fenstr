using static Fenstr.CommonInterop;
using static Fenstr.MouseDragSnapInterop;

namespace Fenstr;

internal sealed class DragSession(
    List<Region> regions,
    List<MonitorEntry> monitors,
    Dictionary<string, OverlayWindow> overlays,
    Dictionary<string, GridPreviewWindow> gridPreviews,
    Config config)
{
    private const int EdgeZonePx = 16;
    private const int MinDivisions = 1;
    private const int MaxDivisions = 10;
    private const int MaximizeRegionIndex = -1;
    private const int SpanRegionIndex = -2;
    public nint DraggedHwnd;
    public Region? ActiveRegion;
    public bool SwallowRmbUp;

    private bool _rmbHeld;
    private bool _shiftHeld;
    private bool _zoneLatched;
    private Region? _spanStartRegion;
    private bool _spanActive;
    private Region? _lockedTarget;

    public bool IsDragActive => DraggedHwnd != 0;
    public bool RmbHeld => _rmbHeld;

    private bool ZoneMode => _zoneLatched;

    public void UpdateConfig(Config cfg)
    {
        config = cfg;
        RefreshActiveOverlayLabel();
    }

    public void BeginDrag(nint hwnd)
    {
        DraggedHwnd = hwnd;
        ActiveRegion = null;
        _rmbHeld = false;
        _shiftHeld = false;
        _zoneLatched = false;
        SwallowRmbUp = false;
        _spanStartRegion = null;
        _spanActive = false;
        _lockedTarget = null;

        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 && GetCursorPos(out var pt))
        {
            _shiftHeld = true;
            BeginSpan(pt);
        }
    }

    public void ResetDragState()
    {
        ActiveRegion = null;
        DraggedHwnd = 0;
        _rmbHeld = false;
        _shiftHeld = false;
        _zoneLatched = false;
        SwallowRmbUp = false;
        _spanStartRegion = null;
        _spanActive = false;
        _lockedTarget = null;
    }

    public void OnDragEnd()
    {
        if (ActiveRegion != null && DraggedHwnd != 0)
        {
            if (ActiveRegion.Index == MaximizeRegionIndex)
                ShowWindow(DraggedHwnd, SW_MAXIMIZE);
            else
                WindowSnapper.Snap(DraggedHwnd, ActiveRegion.Rect, config.MaximizeWhenFullScreen);
        }
        HideActiveOverlay();
        HideAllGridPreviews();
        ActiveRegion = null;
        DraggedHwnd = 0;
        _rmbHeld = false;
        _shiftHeld = false;
        _zoneLatched = false;
        _spanStartRegion = null;
        _spanActive = false;
        _lockedTarget = null;
    }

    public void OnRmbDown(POINT pt)
    {
        SwallowRmbUp = true;
        bool wasHeld = _rmbHeld || _shiftHeld;
        _rmbHeld = true;
        if (!wasHeld) BeginSpan(pt);
    }

    public void OnRmbUp(POINT pt)
    {
        _rmbHeld = false;
        if (_shiftHeld) RefreshOverlay(pt);
        else EndSpan(pt);
    }

    public void OnShiftDown(POINT pt)
    {
        bool wasHeld = _rmbHeld || _shiftHeld;
        _shiftHeld = true;
        if (!wasHeld) BeginSpan(pt);
    }

    public void OnShiftUp(POINT pt)
    {
        _shiftHeld = false;
        if (_rmbHeld) RefreshOverlay(pt);
        else EndSpan(pt);
    }

    private void BeginSpan(POINT pt)
    {
        _zoneLatched = true;
        _lockedTarget = null;
        var anchor = RegionHitTester.HitTest(pt, rmbMode: true, monitors, regions, EdgeZonePx);
        _spanStartRegion = (anchor != null && anchor.Index >= 0) ? anchor : null;
        _spanActive = _spanStartRegion != null;
        RefreshOverlay(pt);
    }

    private void EndSpan(POINT pt)
    {
        if (_spanActive && _spanStartRegion is Region start)
        {
            var hit = RegionHitTester.HitTest(pt, rmbMode: true, monitors, regions, EdgeZonePx);
            if (hit != null && hit.Index >= 0
                && hit.MonitorId == start.MonitorId
                && hit.Index != start.Index)
            {
                _lockedTarget = new Region(start.MonitorId, SpanRegionIndex, RegionHitTester.SpanRect(start, hit));
            }
        }
        _spanActive = false;
        _spanStartRegion = null;
        RefreshOverlay(pt);
    }

    public void OnMouseWheel(POINT pt, short delta)
    {
        var m = RegionHitTester.FindMonitor(pt, monitors);
        if (m == null) return;

        bool changed = UpdateDivisions(m.Id, delta > 0 ? +1 : -1);
        if (changed)
        {
            if (_spanStartRegion?.MonitorId == m.Id)
            {
                _spanStartRegion = null;
                _spanActive = false;
            }
            if (_lockedTarget?.MonitorId == m.Id)
                _lockedTarget = null;
        }
        RefreshOverlay(pt);
        if (changed && gridPreviews.TryGetValue(m.Id, out var preview))
            preview.Show(m, regions);
    }

    public Region? ResolveTarget(POINT pt)
    {
        if (_lockedTarget != null)
            return _lockedTarget;

        if (!ZoneMode && !_spanActive && config.MaximizeDragEnabled)
        {
            var maxHit = RegionHitTester.MaximizeHitTest(pt, monitors, regions, config.MaximizeDragWidthPercent);
            if (maxHit != null)
                return maxHit;
        }

        var hit = RegionHitTester.HitTest(pt, ZoneMode, monitors, regions, EdgeZonePx);

        if (_spanActive && _spanStartRegion is Region start)
        {
            if (hit != null && hit.Index >= 0 && hit.MonitorId == start.MonitorId)
            {
                if (hit.Index == start.Index)
                    return start;
                return new Region(start.MonitorId, SpanRegionIndex, RegionHitTester.SpanRect(start, hit));
            }
            return start;
        }

        return hit;
    }

    public void RefreshOverlay(POINT pt)
    {
        var hit = ResolveTarget(pt);
        if (RegionHitTester.SameTarget(hit, ActiveRegion))
        {
            if (hit != null && overlays.TryGetValue(hit.MonitorId, out var ov))
                ov.Show(hit.Rect, LabelForRegion(hit));
            return;
        }
        HideActiveOverlay();
        SetActiveRegion(hit);
    }

    public void SetActiveRegion(Region? hit)
    {
        ActiveRegion = hit;
        ModeState.DragOverlayRegion = (hit != null && hit.Index >= 0) ? hit : null;
        if (hit != null && overlays.TryGetValue(hit.MonitorId, out var overlay))
            overlay.Show(hit.Rect, LabelForRegion(hit));
    }

    public void HideActiveOverlay()
    {
        if (ActiveRegion != null && overlays.TryGetValue(ActiveRegion.MonitorId, out var overlay))
            overlay.Hide();
        ModeState.DragOverlayRegion = null;
    }

    public void RefreshActiveOverlayLabel()
    {
        if (ActiveRegion == null) return;
        if (!overlays.TryGetValue(ActiveRegion.MonitorId, out var overlay)) return;
        overlay.UpdateLabel(LabelForRegion(ActiveRegion));
    }

    private bool UpdateDivisions(string monitorId, int delta)
    {
        int current = config.DivisionsFor(monitorId);
        int next = Math.Clamp(current + delta, MinDivisions, MaxDivisions);
        if (next == current) return false;

        config.SetDivisions(monitorId, next);
        config.Save();

        var m = monitors.First(x => x.Id == monitorId);
        regions.RemoveAll(r => r.MonitorId == monitorId);
        regions.AddRange(MonitorInfo.ComputeRegionsFor(m, next));
        return true;
    }

    private void HideAllGridPreviews()
    {
        foreach (var p in gridPreviews.Values) p.Hide();
    }

    private string? LabelForRegion(Region r)
    {
        if (r.Index < 0) return null;
        if (!config.PlacementHotkeyEnabled) return null;
        int? vk = config.GetHotkey(r.MonitorId, r.Index);
        return vk is int v ? ModeState.VkToLabel(v) : null;
    }
}
