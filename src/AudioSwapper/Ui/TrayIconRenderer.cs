using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// System.Drawing and System.Windows.Media both define Color and PixelFormat.
// This file is the seam between them, so the GDI+ side is always spelled out.
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GdiImageLockMode = System.Drawing.Imaging.ImageLockMode;
using MediaColor = System.Windows.Media.Color;

namespace AudioSwapper.Ui;

/// <summary>
/// Rasterises a <see cref="DeviceIcon"/> into a tray-sized <see cref="Icon"/>.
///
/// The geometry comes from the same path strings the web UI renders, so the
/// icon in the tray is literally the icon in the menu. Drawing it at runtime
/// rather than shipping .ico files means adding an icon is one string, and it
/// lets the glyph recolour itself for a light or dark taskbar.
/// </summary>
internal static class TrayIconRenderer
{
    private const double DesignSize = 24.0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Renders <paramref name="icon"/> at <paramref name="pixelSize"/>.
    ///
    /// The caller owns the result and must call <see cref="Release"/> on it --
    /// the underlying HICON is not freed by <see cref="Icon.Dispose"/> when it
    /// came from <c>Icon.FromHandle</c>, so swapping the tray glyph on every
    /// device change would otherwise leak a GDI handle per switch.
    /// </summary>
    public static Icon Render(DeviceIcon icon, int pixelSize, bool lightBackground)
    {
        pixelSize = Math.Clamp(pixelSize, 16, 64);

        // A strong neutral either way: near-black on a light taskbar, near-white
        // on a dark one. These are --text-primary from each theme; pure black or
        // white looks harsh against the acrylic taskbar.
        var stroke = lightBackground
            ? MediaColor.FromRgb(0x14, 0x1A, 0x22)
            : MediaColor.FromRgb(0xF1, 0xF4, 0xF8);

        var brush = new SolidColorBrush(stroke);
        brush.Freeze();

        // Round caps and joins because every stroke terminus in this icon set is
        // meant to read as soft; square caps look like a different icon family.
        var pen = new System.Windows.Media.Pen(brush, StrokeThicknessFor(pixelSize))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        // One pixel of breathing room so round caps are not clipped at the edge.
        double padding = pixelSize >= 24 ? 1.5 : 1.0;
        double scale = (pixelSize - padding * 2) / DesignSize;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(padding, padding));
            dc.PushTransform(new ScaleTransform(scale, scale));

            foreach (string data in icon.Paths)
            {
                Geometry geometry;
                try
                {
                    geometry = Geometry.Parse(data);
                }
                catch (FormatException)
                {
                    continue; // A malformed path should cost one stroke, not the icon.
                }

                geometry.Freeze();
                dc.DrawGeometry(brush: null, pen, geometry);
            }

            dc.Pop();
            dc.Pop();
        }

        var target = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        return ToIcon(target, pixelSize);
    }

    /// <summary>
    /// Strokes get proportionally heavier as the icon shrinks. A flat 2.0 on the
    /// 24-unit grid renders at 1.33px in a 16px tray slot, which the compositor
    /// washes out to near-invisible grey.
    /// </summary>
    private static double StrokeThicknessFor(int pixelSize) => pixelSize switch
    {
        <= 16 => 2.35,
        <= 20 => 2.15,
        <= 24 => 2.0,
        _ => 1.85,
    };

    private static Icon ToIcon(RenderTargetBitmap source, int size)
    {
        int stride = size * 4;
        var pixels = new byte[stride * size];
        source.CopyPixels(pixels, stride, 0);

        // RenderTargetBitmap gives premultiplied BGRA; GDI+ Format32bppPArgb has
        // exactly that layout, so the buffer copies across without conversion.
        using var bitmap = new Bitmap(size, size, GdiPixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, size, size), GdiImageLockMode.WriteOnly, GdiPixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < size; y++)
            {
                Marshal.Copy(pixels, y * stride, data.Scan0 + y * data.Stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        IntPtr handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    /// <summary>Frees an icon produced by <see cref="Render"/>.</summary>
    public static void Release(Icon? icon)
    {
        if (icon is null) return;

        IntPtr handle = icon.Handle;
        icon.Dispose();
        if (handle != IntPtr.Zero) DestroyIcon(handle);
    }
}
