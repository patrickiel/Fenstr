using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;

namespace Fenstr;

internal record MonitorEntry(string Id, nint Handle, RECT Bounds, RECT WorkArea, bool IsLandscape);

internal record Region(string MonitorId, int Index, RECT Rect);

/// <summary>
/// Enumerates the current display layout, resolves a stable ID for each
/// monitor from its EDID, and slices the work area into the configured
/// number of regions (columns for landscape, rows for portrait).
/// </summary>
internal static class MonitorInfo
{
    public static List<MonitorEntry> EnumerateMonitors()
    {
        var list = new List<MonitorEntry>();
        var seenIds = new Dictionary<string, int>();
        int fallbackIndex = 0;

        MonitorEnumDelegate cb = (nint hMon, nint hdc, ref RECT rc, nint data) =>
        {
            var info = new MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty,
            };
            if (GetMonitorInfo(hMon, ref info))
            {
                string id = ResolveMonitorId(info.szDevice, fallbackIndex);
                if (seenIds.TryGetValue(id, out int dup))
                {
                    seenIds[id] = dup + 1;
                    id = $"{id}#{dup + 1}";
                }
                else
                {
                    seenIds[id] = 0;
                }

                bool landscape = info.rcWork.Width >= info.rcWork.Height;
                list.Add(new MonitorEntry(id, hMon, info.rcMonitor, info.rcWork, landscape));
                fallbackIndex++;
            }
            return true;
        };

        EnumDisplayMonitors(0, 0, cb, 0);
        GC.KeepAlive(cb);
        return list;
    }

    private static string ResolveMonitorId(string szDevice, int fallbackIndex)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (EnumDisplayDevices(szDevice, 0, ref dd, 0) && !string.IsNullOrEmpty(dd.DeviceID))
        {
            // DeviceID looks like "MONITOR\GSM5B7E\{4d36e96e-...}\0001".
            // Strip the class GUID; keep manufacturer/model + instance.
            var parts = dd.DeviceID.Split('\\');
            if (parts.Length >= 4 && parts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
                return $"{parts[1]}_{parts[^1]}";
            return dd.DeviceID;
        }
        return string.IsNullOrEmpty(szDevice) ? $"monitor-{fallbackIndex}" : szDevice;
    }

    public static List<Region> ComputeRegions(List<MonitorEntry> monitors, Config cfg)
    {
        var regions = new List<Region>();
        foreach (var m in monitors)
            regions.AddRange(ComputeRegionsFor(m, cfg.DivisionsFor(m.Id)));
        return regions;
    }

    public static List<Region> ComputeRegionsFor(MonitorEntry m, int divisions)
    {
        if (divisions < 1) divisions = 1;

        var regions = new List<Region>(divisions);
        for (int i = 0; i < divisions; i++)
        {
            RECT r;
            if (m.IsLandscape)
            {
                int w = m.WorkArea.Width / divisions;
                int left = m.WorkArea.Left + i * w;
                int right = (i == divisions - 1) ? m.WorkArea.Right : left + w;
                r = new RECT { Left = left, Top = m.WorkArea.Top, Right = right, Bottom = m.WorkArea.Bottom };
            }
            else
            {
                int h = m.WorkArea.Height / divisions;
                int top = m.WorkArea.Top + i * h;
                int bottom = (i == divisions - 1) ? m.WorkArea.Bottom : top + h;
                r = new RECT { Left = m.WorkArea.Left, Top = top, Right = m.WorkArea.Right, Bottom = bottom };
            }
            regions.Add(new Region(m.Id, i, r));
        }
        return regions;
    }
}
