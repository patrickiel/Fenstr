using static Fenstr.CommonInterop;

namespace Fenstr;

internal static class RegionHitTester
{
    private const int MaximizeZonePx = 6;
    private const int MaximizeRegionIndex = -1;

    public static Region? MaximizeHitTest(POINT pt, List<MonitorEntry> monitors, List<Region> regions, int widthPercent)
    {
        foreach (var m in monitors)
        {
            if (pt.X < m.Bounds.Left || pt.X >= m.Bounds.Right ||
                pt.Y < m.Bounds.Top  || pt.Y >= m.Bounds.Bottom)
                continue;

            var wa = m.WorkArea;
            if (pt.Y < wa.Top || pt.Y >= wa.Top + MaximizeZonePx)
                return null;

            var first = regions.Find(r => r.MonitorId == m.Id);
            if (first == null) return null;

            int zoneW = first.Rect.Width * Math.Clamp(widthPercent, 10, 90) / 100;
            int centerX = wa.Left + wa.Width / 2;
            if (pt.X >= centerX - zoneW / 2 && pt.X < centerX + (zoneW - zoneW / 2))
                return new Region(m.Id, MaximizeRegionIndex, wa);

            return null;
        }
        return null;
    }

    public static MonitorEntry? FindMonitor(POINT pt, List<MonitorEntry> monitors)
    {
        foreach (var m in monitors)
        {
            if (pt.X >= m.Bounds.Left && pt.X < m.Bounds.Right &&
                pt.Y >= m.Bounds.Top && pt.Y < m.Bounds.Bottom)
                return m;
        }
        return null;
    }

    public static Region? HitTest(POINT pt, bool rmbMode, List<MonitorEntry> monitors, List<Region> regions, int edgeZonePx)
    {
        foreach (var m in monitors)
        {
            if (pt.X < m.Bounds.Left || pt.X >= m.Bounds.Right ||
                pt.Y < m.Bounds.Top  || pt.Y >= m.Bounds.Bottom)
                continue;

            var wa = m.WorkArea;
            pt = new POINT
            {
                X = Math.Clamp(pt.X, wa.Left, wa.Right - 1),
                Y = Math.Clamp(pt.Y, wa.Top, wa.Bottom - 1),
            };

            if (rmbMode)
            {
                foreach (var r in regions)
                {
                    if (r.MonitorId != m.Id) continue;
                    if (pt.X >= r.Rect.Left && pt.X < r.Rect.Right &&
                        pt.Y >= r.Rect.Top && pt.Y < r.Rect.Bottom)
                        return r;
                }
                return null;
            }

            foreach (var r in regions)
            {
                if (r.MonitorId != m.Id) continue;
                if (pt.X < r.Rect.Left || pt.X >= r.Rect.Right ||
                    pt.Y < r.Rect.Top || pt.Y >= r.Rect.Bottom)
                    continue;

                if (r.Rect.Top == wa.Top && pt.Y - r.Rect.Top < edgeZonePx) return r;
                if (r.Rect.Bottom == wa.Bottom && r.Rect.Bottom - pt.Y <= edgeZonePx) return r;
                if (r.Rect.Left == wa.Left && pt.X - r.Rect.Left < edgeZonePx) return r;
                if (r.Rect.Right == wa.Right && r.Rect.Right - pt.X <= edgeZonePx) return r;
            }
            return null;
        }
        return null;
    }

    public static RECT SpanRect(Region a, Region b) => new RECT
    {
        Left = Math.Min(a.Rect.Left, b.Rect.Left),
        Top = Math.Min(a.Rect.Top, b.Rect.Top),
        Right = Math.Max(a.Rect.Right, b.Rect.Right),
        Bottom = Math.Max(a.Rect.Bottom, b.Rect.Bottom),
    };

    public static bool SameTarget(Region? a, Region? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.MonitorId == b.MonitorId
            && a.Rect.Left == b.Rect.Left && a.Rect.Top == b.Rect.Top
            && a.Rect.Right == b.Rect.Right && a.Rect.Bottom == b.Rect.Bottom;
    }
}
