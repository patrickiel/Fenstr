using static Fenstr.CommonInterop;

namespace Fenstr;

/// <summary>
/// Shared state between the drag tracker and the keyboard hook, plus small
/// helpers for mapping virtual-key codes to the labels shown on overlays.
/// </summary>
internal static class ModeState
{
    public static Region? DragOverlayRegion;
    public static bool PlacementModeActive;
    public static nint PlacementTargetHwnd;

    public static string? VkToLabel(int vk)
    {
        if (vk >= VK_A && vk <= VK_Z) return ((char)('A' + (vk - VK_A))).ToString();
        if (vk >= VK_0 && vk <= VK_9) return ((char)('0' + (vk - VK_0))).ToString();
        if (vk >= VK_F1 && vk <= VK_F12) return "F" + (vk - VK_F1 + 1);
        return null;
    }

    public static bool IsValidHotkeyVk(int vk)
    {
        if (vk >= VK_A && vk <= VK_Z) return true;
        if (vk >= VK_0 && vk <= VK_9) return true;
        if (vk >= VK_F1 && vk <= VK_F12) return true;
        return false;
    }
}
