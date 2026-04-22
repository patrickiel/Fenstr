using System.Diagnostics;
using Microsoft.Win32;
using static Fenstr.Win11SnapGuardInterop;

namespace Fenstr;

/// <summary>
/// Disables the Win11 built-in window-arrangement snap while the app runs.
/// Writes a sentinel file on disable so a prior crash can be detected and
/// the user's original setting restored on next launch.
/// </summary>
internal static class SnapGuard
{
    private const string DesktopKeyPath = @"Control Panel\Desktop";
    private const string ExplorerAdvancedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private static readonly string[] ArrangementValues =
    {
        "WindowArrangementActive",
    };

    private static readonly string[] AlwaysOnValues =
    {
        "DockMoving",
        "SnapSizing",
    };

    private static readonly string[] SnapAssistValues =
    {
        "EnableSnapAssistFlyout",
        "EnableSnapBar",
        "SnapAssist",
    };

    private static readonly string SentinelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Fenstr", "snap-disabled.flag");

    public static void RecoverFromPriorCrash()
    {
        if (File.Exists(SentinelPath))
        {
            Enable();
            TryDelete(SentinelPath);
        }
    }

    public static void Disable()
    {
        WriteSentinel();
        SetArrangement(enabled: false);
    }

    public static void Restore()
    {
        Enable();
        TryDelete(SentinelPath);
    }

    private static void Enable() => SetArrangement(enabled: true);

    private static void SetArrangement(bool enabled)
    {
        var str = enabled ? "1" : "0";
        var dword = enabled ? 1 : 0;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKeyPath, writable: true);
            if (key != null)
            {
                foreach (var name in ArrangementValues)
                    key.SetValue(name, str, RegistryValueKind.String);
                foreach (var name in AlwaysOnValues)
                    key.SetValue(name, "1", RegistryValueKind.String);
            }
        }
        catch
        {
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedPath, writable: true);
            if (key != null)
            {
                foreach (var name in SnapAssistValues)
                    key.SetValue(name, dword, RegistryValueKind.DWord);
            }
        }
        catch
        {
        }

        try
        {
            SystemParametersInfo(
                SPI_SETWINARRANGING, 0, (nint)dword,
                SPIF_SENDCHANGE);
        }
        catch
        {
        }

        try
        {
            using var ps = Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "USER32.DLL,UpdatePerUserSystemParameters 1, True",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
            ps?.WaitForExit(2000);
        }
        catch
        {
        }

        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, null, SMTO_ABORTIFHUNG, 100, out _);
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "Policy", SMTO_ABORTIFHUNG, 100, out _);
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 1, null, SMTO_ABORTIFHUNG, 100, out _);
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 1, "Policy", SMTO_ABORTIFHUNG, 100, out _);
    }

    private static void WriteSentinel()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SentinelPath)!);
            File.WriteAllText(SentinelPath, string.Empty);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
