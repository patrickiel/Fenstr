using System.Runtime.InteropServices;

namespace Fenstr;

/// <summary>
/// Win32 declarations for the WinEvent move/size hook, the low-level mouse
/// hook, and the drag-time overlay and grid-preview windows.
/// </summary>
internal static partial class MouseDragSnapInterop
{
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_PAINT = 0x000F;
    public const int WM_SETTINGCHANGE = 0x001A;

    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const uint WM_NCLBUTTONDOWN = 0x00A1;
    public const uint GA_ROOT = 2;
    public const uint LLMHF_INJECTED = 0x00000001;
    public const int SM_CYCAPTION = 4;
    public const int SM_CYFRAME = 33;
    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;
    public const int SM_CXPADDEDBORDER = 92;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public const int WS_POPUP = unchecked((int)0x80000000);

    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint LWA_ALPHA = 0x00000002;

    public const int SW_HIDE = 0;

    public static readonly nint HWND_TOPMOST = -1;

    public const int TRANSPARENT = 1;
    public const uint DT_CENTER = 0x00000001;
    public const uint DT_VCENTER = 0x00000004;
    public const uint DT_SINGLELINE = 0x00000020;
    public const uint DT_NOPREFIX = 0x00000800;
    public const int FW_BOLD = 700;
    public const int ANSI_CHARSET = 0;
    public const int OUT_DEFAULT_PRECIS = 0;
    public const int CLIP_DEFAULT_PRECIS = 0;
    public const int CLEARTYPE_QUALITY = 5;
    public const int DEFAULT_PITCH = 0;
    public const int FF_SWISS = 0x20;

    public const int RGN_OR = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public CommonInterop.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public CommonInterop.RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate nint WndProcDelegate(nint hwnd, uint msg, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void TimerProcDelegate(nint hwnd, uint uMsg, nuint nIDEvent, uint dwTime);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate nint LowLevelMouseProc(int nCode, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out CommonInterop.POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, TimerProcDelegate? lpTimerFunc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawText(nint hDC, string lpString, int nCount, ref CommonInterop.RECT lpRect, uint uFormat);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint hObject);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint hgdiobj);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int iBkMode);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint crColor);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontIndirectW")]
    public static extern nint CreateFontIndirect(ref LOGFONT lplf);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [LibraryImport("gdi32.dll")]
    public static partial int CombineRgn(nint hrgnDst, nint hrgnSrc1, nint hrgnSrc2, int fnCombineMode);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(CommonInterop.POINT point);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint gaFlags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern nint SendMessageTimeout(nint hWnd, uint msg, nuint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);
}
