<#
.SYNOPSIS
Clicks at physical screen coordinates, then puts the cursor back.

Only used for testing the WebView2 UI, which cannot be driven any other way from
outside the process. The cursor is restored so a click does not leave the mouse
parked somewhere unexpected on a machine somebody is using.
#>
param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [int]$SettleMs = 450
)

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Click
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

    const uint LeftDown = 0x0002;
    const uint LeftUp = 0x0004;

    public static POINT At(int x, int y)
    {
        POINT before;
        GetCursorPos(out before);

        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(LeftDown, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(45);
        mouse_event(LeftUp, 0, 0, 0, IntPtr.Zero);

        return before;
    }
}
'@

# Must match the app's per-monitor awareness or the coordinates are virtualised.
[void][Click]::SetProcessDpiAwarenessContext([IntPtr](-4))

$before = [Click]::At($X, $Y)
Start-Sleep -Milliseconds $SettleMs
[void][Click]::SetCursorPos($before.X, $before.Y)

"clicked ({0},{1}); cursor restored to ({2},{3})" -f $X, $Y, $before.X, $before.Y
