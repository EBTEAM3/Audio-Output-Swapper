using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AudioSwapper.Ui;

// Only Geometry is needed from WPF here; importing the whole namespace collides
// with System.Drawing on Color and PixelFormat.
using Geometry = System.Windows.Media.Geometry;

namespace AudioSwapper.Tools;

/// <summary>
/// Renders every device icon at real tray sizes, on both a dark and a light
/// strip, and writes one contact sheet. Run it after touching any icon path.
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = { 16, 20, 24, 32 };

    [STAThread]
    private static int Main(string[] args)
    {
        string outputPath = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "tray-icons.png");

        // Fail loudly if a path string is malformed. The app deliberately
        // swallows this so one bad glyph cannot take the tray down, which means
        // this harness is the only place it would ever be noticed.
        int broken = 0;
        foreach (var icon in DeviceIcons.All)
        {
            foreach (string data in icon.Paths)
            {
                try
                {
                    Geometry.Parse(data);
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine($"BAD PATH  {icon.Key}: {ex.Message}");
                    Console.Error.WriteLine($"          {data}");
                    broken++;
                }
            }
        }

        const int cell = 44;
        const int labelWidth = 150;
        int columns = Sizes.Length * 2;              // each size, dark then light
        int width = labelWidth + columns * cell + 20;
        int height = 30 + DeviceIcons.All.Count * cell;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(0xFF, 0x17, 0x1C, 0x24));

        using var font = new Font("Segoe UI", 8f);
        using var headerBrush = new SolidBrush(Color.FromArgb(0xFF, 0xAA, 0xB4, 0xC1));
        using var lightStrip = new SolidBrush(Color.FromArgb(0xFF, 0xF1, 0xF5, 0xFB));

        // Light half of the sheet, so the light-taskbar glyph colour is checked too.
        int lightStart = labelWidth + Sizes.Length * cell;
        g.FillRectangle(lightStrip, lightStart, 0, Sizes.Length * cell, height);

        for (int c = 0; c < columns; c++)
        {
            int size = Sizes[c % Sizes.Length];
            bool light = c >= Sizes.Length;
            using var brush = new SolidBrush(light
                ? Color.FromArgb(0xFF, 0x56, 0x61, 0x73)
                : Color.FromArgb(0xFF, 0xAA, 0xB4, 0xC1));
            g.DrawString($"{size}px", font, brush, labelWidth + c * cell + 6, 8);
        }

        for (int r = 0; r < DeviceIcons.All.Count; r++)
        {
            var icon = DeviceIcons.All[r];
            int y = 30 + r * cell;

            g.DrawString(icon.Label, font, headerBrush, 8, y + cell / 2f - 8);

            for (int c = 0; c < columns; c++)
            {
                int size = Sizes[c % Sizes.Length];
                bool light = c >= Sizes.Length;

                var rendered = TrayIconRenderer.Render(icon, size, light);
                try
                {
                    using var bitmap = rendered.ToBitmap();
                    g.DrawImage(bitmap,
                        labelWidth + c * cell + (cell - size) / 2,
                        y + (cell - size) / 2,
                        size, size);
                }
                finally
                {
                    TrayIconRenderer.Release(rendered);
                }
            }
        }

        sheet.Save(outputPath, ImageFormat.Png);
        Console.WriteLine($"{outputPath} ({width}x{height}), {DeviceIcons.All.Count} icons, {broken} bad paths");

        return broken == 0 ? 0 : 1;
    }
}
