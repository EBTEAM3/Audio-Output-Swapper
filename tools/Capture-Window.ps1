<#
.SYNOPSIS
Captures only the AudioSwapper windows, by handle, to a PNG.

Grabbing the whole screen would sweep up whatever the user happens to have open.
This finds the app's own top-level windows and captures just their bounds, with a
small bleed so the DWM rounded corners and drop shadow are visible.
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Bleed = 18,
    [double]$Scale = 1.0,
    [string]$ProcessName = 'AudioSwapper'
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# PowerShell starts DPI-unaware, so window rects and screen bounds come back
# virtualised while the app itself is per-monitor aware and reports physical
# pixels. Without this the two coordinate spaces disagree and the capture is
# offset and clipped on any display that is not at 100%.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DpiOptIn
{
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
}
'@
[void][DpiOptIn]::SetProcessDpiAwarenessContext([IntPtr](-4))  # PER_MONITOR_AWARE_V2

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class WinEnum
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lparam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    // DWMWA_EXTENDED_FRAME_BOUNDS -- the visible frame, excluding the invisible
    // resize border GetWindowRect reports.
    const int ExtendedFrameBounds = 9;

    public static List<object[]> Visible(uint pid)
    {
        var found = new List<object[]>();
        EnumWindows((hwnd, l) =>
        {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != pid || !IsWindowVisible(hwnd)) return true;

            RECT r;
            if (DwmGetWindowAttribute(hwnd, ExtendedFrameBounds, out r, Marshal.SizeOf(typeof(RECT))) != 0)
            {
                if (!GetWindowRect(hwnd, out r)) return true;
            }

            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w < 40 || h < 40) return true;   // skip the hidden message window

            var sb = new StringBuilder(GetWindowTextLength(hwnd) + 1);
            GetWindowText(hwnd, sb, sb.Capacity);

            found.Add(new object[] { hwnd, r.Left, r.Top, w, h, sb.ToString() });
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@ -ReferencedAssemblies System.Drawing, System.Windows.Forms

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { "no $ProcessName process"; exit 1 }

$windows = [WinEnum]::Visible([uint32]$proc.Id)
if ($windows.Count -eq 0) { "no visible windows"; exit 2 }

# Union of every visible window, so a toast and a menu shown together land in
# one capture rather than two.
$minX = ($windows | ForEach-Object { $_[1] } | Measure-Object -Minimum).Minimum
$minY = ($windows | ForEach-Object { $_[2] } | Measure-Object -Minimum).Minimum
$maxX = ($windows | ForEach-Object { $_[1] + $_[3] } | Measure-Object -Maximum).Maximum
$maxY = ($windows | ForEach-Object { $_[2] + $_[4] } | Measure-Object -Maximum).Maximum

foreach ($w in $windows) { "window: {0}x{1} at {2},{3}" -f $w[3], $w[4], $w[1], $w[2] }

# The virtual screen, not the primary one: these windows anchor to whichever
# monitor the cursor is on, which is regularly not the primary.
$screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
$left = [Math]::Max($screen.Left, $minX - $Bleed)
$top = [Math]::Max($screen.Top, $minY - $Bleed)
$right = [Math]::Min($screen.Right, $maxX + $Bleed)
$bottom = [Math]::Min($screen.Bottom, $maxY + $Bleed)
$width = $right - $left
$height = $bottom - $top

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size $width, $height))
$g.Dispose()

if ($Scale -ne 1.0) {
    $sw = [int]($width * $Scale); $sh = [int]($height * $Scale)
    $scaled = New-Object System.Drawing.Bitmap $sw, $sh
    $sg = [System.Drawing.Graphics]::FromImage($scaled)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.DrawImage($bitmap, 0, 0, $sw, $sh)
    $sg.Dispose(); $bitmap.Dispose(); $bitmap = $scaled
}

$bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
$out = "saved {0} ({1}x{2})" -f $Path, $bitmap.Width, $bitmap.Height
$bitmap.Dispose()
$out
