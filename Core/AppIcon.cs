using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FlameshotClipboardHelper;

internal static class AppIcon
{
    private static Icon? _cached;

    public static Icon Tray => _cached ??= LoadOrCreate();

    private static Icon LoadOrCreate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(path))
        {
            try
            {
                return new Icon(path);
            }
            catch
            {
                // fall through
            }
        }

        return CreateTrayIcon();
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var blue = Color.FromArgb(0, 122, 204);
            using var body = new SolidBrush(blue);
            using var clip = new SolidBrush(Color.FromArgb(0, 90, 168));

            g.FillRectangle(clip, 11, 3, 10, 5);
            g.FillRectangle(body, 6, 5, 20, 24);
            g.FillRectangle(Brushes.White, 9, 9, 14, 16);

            using var pen = new Pen(blue, 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLine(pen, 12, 17, 15, 21);
            g.DrawLine(pen, 15, 21, 22, 13);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
