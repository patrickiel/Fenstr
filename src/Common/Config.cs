using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fenstr;

internal record RegionHotkeyEntry(int Index, int VkCode);

internal record MonitorConfig(string Id, int Divisions, List<RegionHotkeyEntry>? Hotkeys = null);

internal record HotkeyBinding(int VkCode, bool Win = false, bool Ctrl = false, bool Alt = false, bool Shift = false);

/// <summary>
/// On-disk JSON config: per-monitor division count, region-to-hotkey
/// bindings, and the global placement-mode hotkey. Lives under
/// %AppData%\Fenstr.
/// </summary>
internal record Config(
    List<MonitorConfig> Monitors,
    HotkeyBinding? PlacementHotkey = null,
    bool PlacementHotkeyEnabled = true,
    bool MaximizeDragEnabled = true,
    int MaximizeDragWidthPercent = 50)
{
    public const int DefaultDivisions = 2;

    public static HotkeyBinding DefaultPlacementHotkey { get; } = new(0x20 /* VK_SPACE */, Win: true, Ctrl: true);

    public HotkeyBinding PlacementHotkeyOrDefault => PlacementHotkey ?? DefaultPlacementHotkey;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Fenstr", "config.json");

    public static Config Load()
    {
        var path = FilePath;
        if (!File.Exists(path))
            return new Config([]);

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.Config)
                   ?? new Config([]);
        }
        catch
        {
            return new Config([]);
        }
    }

    public int DivisionsFor(string monitorId)
    {
        foreach (var m in Monitors)
            if (m.Id == monitorId) return m.Divisions;
        return DefaultDivisions;
    }

    public void SetDivisions(string monitorId, int divisions)
    {
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (Monitors[i].Id == monitorId)
            {
                Monitors[i] = Monitors[i] with { Divisions = divisions };
                return;
            }
        }
        Monitors.Add(new MonitorConfig(monitorId, divisions));
    }

    public int? GetHotkey(string monitorId, int regionIndex)
    {
        foreach (var m in Monitors)
        {
            if (m.Id != monitorId) continue;
            if (m.Hotkeys == null) return null;
            foreach (var h in m.Hotkeys)
                if (h.Index == regionIndex) return h.VkCode;
            return null;
        }
        return null;
    }

    public void SetHotkey(string monitorId, int regionIndex, int vkCode)
    {
        for (int i = 0; i < Monitors.Count; i++)
            Monitors[i].Hotkeys?.RemoveAll(h => h.VkCode == vkCode);

        int monIdx = -1;
        for (int i = 0; i < Monitors.Count; i++)
            if (Monitors[i].Id == monitorId) { monIdx = i; break; }

        if (monIdx < 0)
        {
            Monitors.Add(new MonitorConfig(monitorId, DefaultDivisions, [new RegionHotkeyEntry(regionIndex, vkCode)]));
            return;
        }

        var target = Monitors[monIdx];
        if (target.Hotkeys == null)
        {
            Monitors[monIdx] = target with { Hotkeys = [new RegionHotkeyEntry(regionIndex, vkCode)] };
        }
        else
        {
            target.Hotkeys.RemoveAll(h => h.Index == regionIndex);
            target.Hotkeys.Add(new RegionHotkeyEntry(regionIndex, vkCode));
        }
    }

    public void RemoveHotkey(string monitorId, int regionIndex)
    {
        foreach (var m in Monitors)
        {
            if (m.Id != monitorId) continue;
            m.Hotkeys?.RemoveAll(h => h.Index == regionIndex);
            return;
        }
    }

    public (string MonitorId, int Index)? FindRegionByHotkey(int vkCode)
    {
        foreach (var m in Monitors)
        {
            if (m.Hotkeys == null) continue;
            foreach (var h in m.Hotkeys)
                if (h.VkCode == vkCode) return (m.Id, h.Index);
        }
        return null;
    }

    public void Save()
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, ConfigJsonContext.Default.Config);
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigJsonContext : JsonSerializerContext { }
