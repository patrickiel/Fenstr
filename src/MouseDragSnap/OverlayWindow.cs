using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;
using static Fenstr.MouseDragSnapInterop;

namespace Fenstr;

/// <summary>
/// Per-monitor translucent overlay drawn over the target region during a
/// drag-snap. Shows the region's hotkey letter when one is assigned.
/// </summary>
internal sealed class OverlayWindow
{
    private const string ClassName = "FenstrOverlay";
    private const uint OverlayColorBgr = 0x00FF7800u;
    private const uint LabelTextColorBgr = 0x00FFFFFFu;
    private const byte OverlayAlpha = 0x40;

    private static readonly WndProcDelegate s_wndProc = WndProc;
    private static readonly Dictionary<nint, string?> s_labels = [];
    private static bool s_classRegistered;
    private static nint s_brush;
    private static nint s_labelFont;
    private static int s_labelFontSize;

    public static event Action? ImmersiveColorChanged;

    public nint Hwnd { get; }
    public string MonitorId { get; }

    private OverlayWindow(nint hwnd, string monitorId)
    {
        Hwnd = hwnd;
        MonitorId = monitorId;
    }

    public static OverlayWindow Create(string monitorId, RECT monitorBounds)
    {
        EnsureClassRegistered();

        var hInstance = GetModuleHandle(null);
        var hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            ClassName,
            string.Empty,
            WS_POPUP,
            monitorBounds.Left, monitorBounds.Top,
            monitorBounds.Width, monitorBounds.Height,
            0, 0, hInstance, 0);

        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

        SetLayeredWindowAttributes(hwnd, 0, OverlayAlpha, LWA_ALPHA);
        return new OverlayWindow(hwnd, monitorId);
    }

    public void Show(RECT region, string? label = null)
    {
        s_labels[Hwnd] = label;
        SetWindowPos(Hwnd, HWND_TOPMOST,
            region.Left, region.Top, region.Width, region.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(Hwnd, 0, true);
    }

    public void UpdateLabel(string? label)
    {
        s_labels[Hwnd] = label;
        InvalidateRect(Hwnd, 0, true);
    }

    public void Hide()
    {
        s_labels.Remove(Hwnd);
        ShowWindow(Hwnd, SW_HIDE);
    }

    public void Destroy()
    {
        s_labels.Remove(Hwnd);
        if (Hwnd != 0) DestroyWindow(Hwnd);
    }

    private static void EnsureClassRegistered()
    {
        if (s_classRegistered) return;

        s_brush = CreateSolidBrush(OverlayColorBgr);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = s_wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = s_brush,
        };

        if (RegisterClassEx(ref wc) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");

        s_classRegistered = true;
    }

    private static nint GetLabelFont(int pixelHeight)
    {
        if (s_labelFont != 0 && s_labelFontSize == pixelHeight) return s_labelFont;
        if (s_labelFont != 0) DeleteObject(s_labelFont);

        var lf = new LOGFONT
        {
            lfHeight = -pixelHeight,
            lfWeight = FW_BOLD,
            lfCharSet = ANSI_CHARSET,
            lfOutPrecision = OUT_DEFAULT_PRECIS,
            lfClipPrecision = CLIP_DEFAULT_PRECIS,
            lfQuality = CLEARTYPE_QUALITY,
            lfPitchAndFamily = DEFAULT_PITCH | FF_SWISS,
            lfFaceName = "Segoe UI",
        };
        s_labelFont = CreateFontIndirect(ref lf);
        s_labelFontSize = pixelHeight;
        return s_labelFont;
    }

    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WM_PAINT)
        {
            BeginPaint(hwnd, out var ps);
            if (s_labels.TryGetValue(hwnd, out var label) && !string.IsNullOrEmpty(label)
                && GetWindowRect(hwnd, out var wr))
            {
                int h = wr.Bottom - wr.Top;
                int w = wr.Right - wr.Left;
                int size = Math.Max(24, Math.Min(w, h) / 2);
                var font = GetLabelFont(size);
                var prevFont = SelectObject(ps.hdc, font);
                SetBkMode(ps.hdc, TRANSPARENT);
                SetTextColor(ps.hdc, LabelTextColorBgr);
                var rect = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
                DrawText(ps.hdc, label!, label!.Length, ref rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
                SelectObject(ps.hdc, prevFont);
            }
            EndPaint(hwnd, ref ps);
            return 0;
        }
        if (msg == WM_SETTINGCHANGE && lParam != 0 &&
            Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            ImmersiveColorChanged?.Invoke();
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
