using System;
using System.Runtime.InteropServices;

namespace AudioSwapper.Interop;

/// <summary>
/// Win32 window tweaks WPF does not expose: DWM rounded corners, click-through
/// / no-activate toolwindows, and taskbar-aware screen geometry.
/// </summary>
internal static class NativeWindowHelpers
{
    // --- Window styles ------------------------------------------------------
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080; // keep out of Alt+Tab
    private const int WsExNoActivate = 0x08000000; // never steal focus
    private const int WsExTopmost = 0x00000008;

    // --- DwmSetWindowAttribute ---------------------------------------------
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    /// <summary>DWMWA_COLOR_NONE -- suppresses the 1px border DWM draws.</summary>
    private const uint DwmColorNone = 0xFFFFFFFE;

    private enum CornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    /// <summary>
    /// Asks the Windows 11 compositor to round this window. Unlike clipping with
    /// SetWindowRgn, DWM anti-aliases the corners and keeps the drop shadow --
    /// which matters here because nothing in this UI has a square corner and a
    /// hard-clipped edge reads as a jaggy box.
    ///
    /// Silently does nothing before Windows 11 build 22000; the window is simply
    /// square there.
    /// </summary>
    public static void ApplyRoundedCorners(IntPtr hwnd, bool small = false)
    {
        if (hwnd == IntPtr.Zero) return;

        int preference = (int)(small ? CornerPreference.RoundSmall : CornerPreference.Round);
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));

        // Without this, DWM outlines the window in the system border colour,
        // which cuts a hard line right where the glass edge should be.
        uint none = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(uint));
    }

    /// <summary>
    /// Makes a window behave like a notification: never takes focus, never
    /// appears in Alt+Tab or the taskbar. Applied to the toast so a switch mid
    /// game or mid call cannot yank the foreground away.
    /// </summary>
    public static void MakeNonActivatingToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        int style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow | WsExNoActivate | WsExTopmost);
    }

    /// <summary>
    /// Keeps a window out of Alt+Tab without blocking focus. Used for the menu,
    /// which does need keyboard input but should not look like a running app.
    /// </summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        int style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow);
    }
}
