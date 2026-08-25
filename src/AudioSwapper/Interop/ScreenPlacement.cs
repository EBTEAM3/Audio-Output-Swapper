using System;
using System.Runtime.InteropServices;

namespace AudioSwapper.Interop;

/// <summary>
/// Places windows in physical pixels against the correct monitor's work area.
///
/// WPF's Left/Top/Width/Height are device-independent units resolved against one
/// notional scale, which lands windows in the wrong place on a mixed-DPI desktop
/// (a 150% laptop panel beside a 100% external monitor). Doing the arithmetic in
/// physical pixels against the target monitor's own DPI is the only way to get
/// "just above the taskbar, hard against the right edge" right on every setup.
/// </summary>
internal static class ScreenPlacement
{
    private const int MonitorDefaultToNearest = 0x2;
    private const uint MdtEffectiveDpi = 0;

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
                                            int x, int y, int cx, int cy, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, uint type, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Anchors a window to the bottom-right of the work area on the monitor
    /// under the cursor, which is where the tray the user just clicked lives.
    /// Sizes are given in device-independent units and scaled here.
    /// </summary>
    public static void AnchorBottomRight(IntPtr hwnd, double dipWidth, double dipHeight,
                                         double dipMargin, bool topmost) =>
        AnchorBottomRight(hwnd, dipWidth, dipHeight, dipMargin, dipMargin, topmost);

    /// <summary>
    /// As above, but with independent horizontal and vertical gaps -- the toast
    /// sits well clear of the bottom edge while staying tight to the right one.
    /// </summary>
    public static void AnchorBottomRight(IntPtr hwnd, double dipWidth, double dipHeight,
                                         double dipMarginX, double dipMarginY, bool topmost)
    {
        if (hwnd == IntPtr.Zero) return;

        IntPtr monitor = GetCursorPos(out var cursor)
            ? MonitorFromPoint(cursor, MonitorDefaultToNearest)
            : MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        double scale = GetScale(monitor);

        int width = (int)Math.Round(dipWidth * scale);
        int height = (int)Math.Round(dipHeight * scale);
        int marginX = (int)Math.Round(dipMarginX * scale);
        int marginY = (int)Math.Round(dipMarginY * scale);

        int x = info.Work.Right - width - marginX;
        int y = info.Work.Bottom - height - marginY;

        // Never let a tall menu run off the top of the work area on a short
        // screen; clamping beats being unreachable.
        y = Math.Max(info.Work.Top, y);
        x = Math.Max(info.Work.Left, x);

        uint flags = SwpNoActivate | SwpShowWindow | (topmost ? 0 : SwpNoZOrder);
        SetWindowPos(hwnd, topmost ? HwndTopmost : IntPtr.Zero, x, y, width, height, flags);
    }

    /// <summary>
    /// Anchors a window to the bottom-right without touching its size, using
    /// whatever size it currently has.
    ///
    /// For a window that sizes itself to its content, the height is not known
    /// up front -- it depends on the text, the DPI and the user's font scale.
    /// Passing a guessed height here would either clip the content or leave a
    /// gap, so the window is measured and this only moves it.
    /// </summary>
    public static void AnchorBottomRightKeepSize(IntPtr hwnd, double dipMarginX, double dipMarginY)
    {
        if (hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var window)) return;

        IntPtr monitor = GetCursorPos(out var cursor)
            ? MonitorFromPoint(cursor, MonitorDefaultToNearest)
            : MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        double scale = GetScale(monitor);

        int marginX = (int)Math.Round(dipMarginX * scale);
        int marginY = (int)Math.Round(dipMarginY * scale);

        int x = Math.Max(info.Work.Left, info.Work.Right - window.Width - marginX);
        int y = Math.Max(info.Work.Top, info.Work.Bottom - window.Height - marginY);

        SetWindowPos(hwnd, HwndTopmost, x, y, 0, 0,
                     SwpNoActivate | SwpNoSize | SwpShowWindow);
    }

    /// <summary>
    /// Parks a window far off-screen. WebView2 needs a shown window before it
    /// will attach, so a window whose whole content is a page would otherwise
    /// flash an empty rectangle in the corner for the few hundred milliseconds
    /// the browser takes to boot on first use.
    /// </summary>
    public static void MoveOffScreen(IntPtr hwnd, double dipWidth, double dipHeight)
    {
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000,
                     (int)Math.Round(dipWidth), (int)Math.Round(dipHeight),
                     SwpNoActivate | SwpNoZOrder | SwpShowWindow);
    }

    private static double GetScale(IntPtr monitor)
    {
        // Falls back to 1.0 rather than the system DPI: on a mixed-DPI desktop a
        // wrong scale is worse than no scale, because it compounds with the
        // work-area arithmetic and throws the window off the screen entirely.
        if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
            return dpiX / 96.0;

        return 1.0;
    }
}
