using System.Runtime.InteropServices;
using static Fenstr.CommonInterop;
using static Fenstr.KeyboardShortcutsInterop;

namespace Fenstr;

/// <summary>
/// Per-monitor overlay shown while placement mode is active. Tints each
/// region and draws its assigned hotkey letter so the user can pick one.
/// </summary>
internal sealed class KeyboardSelectionWindow
{
    private const string ClassName = "FenstrKeySelect";
    private const uint OverlayColorBgr = 0x00FF7800u;
    private const uint LabelTextColorBgr = 0x00FFFFFFu;
    private const byte FillAlpha = 0x60;
    private const int BorderGapPx = 4;

    private static readonly WndProcDelegate s_wndProc = WndProc;
    private static readonly Dictionary<nint, List<LabelEntry>> s_labels = [];
    private static bool s_classRegistered;
    private static nint s_brush;
    private static nint s_labelFont;
    private static int s_labelFontSize;

    private record LabelEntry(RECT LocalRect, string Text);

    public nint Hwnd { get; }
    public string MonitorId { get; }

    private KeyboardSelectionWindow(nint hwnd, string monitorId)
    {
        Hwnd = hwnd;
        MonitorId = monitorId;
    }

    public static KeyboardSelectionWindow Create(string monitorId, RECT monitorBounds)
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

        SetLayeredWindowAttributes(hwnd, 0, FillAlpha, LWA_ALPHA);
        return new KeyboardSelectionWindow(hwnd, monitorId);
    }

    public void Show(MonitorEntry monitor, IReadOnlyList<Region> regions, Config cfg)
    {
        var wa = monitor.WorkArea;
        var labels = new List<LabelEntry>();
        nint combined = CreateRectRgn(0, 0, 0, 0);

        foreach (var r in regions)
        {
            if (r.MonitorId != monitor.Id) continue;

            int l = r.Rect.Left - wa.Left;
            int t = r.Rect.Top - wa.Top;
            int rr = r.Rect.Right - wa.Left;
            int bb = r.Rect.Bottom - wa.Top;

            int gl = l + (r.Rect.Left == wa.Left ? 0 : BorderGapPx / 2);
            int gt = t + (r.Rect.Top == wa.Top ? 0 : BorderGapPx / 2);
            int gr = rr - (r.Rect.Right == wa.Right ? 0 : BorderGapPx / 2);
            int gb = bb - (r.Rect.Bottom == wa.Bottom ? 0 : BorderGapPx / 2);

            var strip = CreateRectRgn(gl, gt, gr, gb);
            CombineRgn(combined, combined, strip, RGN_OR);
            DeleteObject(strip);

            int? vk = cfg.GetHotkey(r.MonitorId, r.Index);
            if (vk is int v)
            {
                string? text = ModeState.VkToLabel(v);
                if (text != null)
                    labels.Add(new LabelEntry(new RECT { Left = l, Top = t, Right = rr, Bottom = bb }, text));
            }
        }

        s_labels[Hwnd] = labels;
        SetWindowRgn(Hwnd, combined, true);

        SetWindowPos(Hwnd, HWND_TOPMOST,
            wa.Left, wa.Top, wa.Width, wa.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(Hwnd, 0, true);
    }

    public void Hide()
    {
        if (Hwnd == 0) return;
        s_labels.Remove(Hwnd);
        ShowWindow(Hwnd, SW_HIDE);
        SetWindowRgn(Hwnd, 0, false);
    }

    public void Destroy()
    {
        s_labels.Remove(Hwnd);
        if (Hwnd != 0) DestroyWindow(Hwnd);
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

    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WM_PAINT)
        {
            BeginPaint(hwnd, out var ps);
            if (s_labels.TryGetValue(hwnd, out var labels) && labels.Count > 0)
            {
                SetBkMode(ps.hdc, TRANSPARENT);
                SetTextColor(ps.hdc, LabelTextColorBgr);
                foreach (var entry in labels)
                {
                    int w = entry.LocalRect.Right - entry.LocalRect.Left;
                    int h = entry.LocalRect.Bottom - entry.LocalRect.Top;
                    int size = Math.Max(24, Math.Min(w, h) / 2);
                    var font = GetLabelFont(size);
                    var prev = SelectObject(ps.hdc, font);
                    var rect = entry.LocalRect;
                    DrawText(ps.hdc, entry.Text, entry.Text.Length, ref rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
                    SelectObject(ps.hdc, prev);
                }
            }
            EndPaint(hwnd, ref ps);
            return 0;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
