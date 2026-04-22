using System.Runtime.InteropServices;

namespace Fenstr;

/// <summary>
/// Win32 declarations used by SnapGuard to toggle the window-arrangement
/// system parameter and broadcast the setting change.
/// </summary>
internal static partial class Win11SnapGuardInterop
{
    public const int WM_SETTINGCHANGE = 0x001A;

    public const uint SPI_SETWINARRANGING = 0x0083;
    public const uint SPIF_UPDATEINIFILE = 0x0001;
    public const uint SPIF_SENDCHANGE = 0x0002;

    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public static readonly nint HWND_BROADCAST = 0xffff;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessageTimeout(nint hWnd, int msg, nint wParam, string? lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);
}
