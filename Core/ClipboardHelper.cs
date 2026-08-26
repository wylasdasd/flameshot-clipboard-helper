using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace FlameshotClipboardHelper;

internal static class ClipboardHelper
{
    private static readonly object Gate = new();

    // lazy: skip bitmap for huge files to avoid OOM; file-drop still gives unique paste name
    private const long MaxBitmapFileBytes = 15 * 1024 * 1024;
    private const long MaxPixelCount = 3840L * 2160L;

    public static bool TryPushScreenshot(string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!WaitForStableFile(filePath))
            return false;

        lock (Gate)
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    var data = BuildClipboardData(filePath);
                    Clipboard.SetDataObject(data, copy: true);
                    return true;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(100);
                }
                catch (ExternalException) when (attempt < 5)
                {
                    Thread.Sleep(100);
                }
                catch (OutOfMemoryException) when (attempt < 5)
                {
                    GC.Collect();
                    Thread.Sleep(100);
                }
            }

            try
            {
                Clipboard.SetDataObject(BuildFileDropOnly(filePath), copy: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static DataObject BuildClipboardData(string filePath)
    {
        var data = BuildFileDropOnly(filePath);
        TryAddImage(data, filePath);
        return data;
    }

    private static DataObject BuildFileDropOnly(string filePath)
    {
        var data = new DataObject();
        var files = new StringCollection();
        files.Add(filePath);
        data.SetFileDropList(files);
        return data;
    }

    private static void TryAddImage(DataObject data, string filePath)
    {
        var length = new FileInfo(filePath).Length;
        if (length <= 0 || length > MaxBitmapFileBytes)
            return;

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            if ((long)img.Width * img.Height > MaxPixelCount)
                return;

            data.SetImage(new Bitmap(img));
        }
        catch (OutOfMemoryException)
        {
            // File reference alone is still usable.
        }
        catch (ArgumentException)
        {
            // PNG not fully written yet; retry loop handles that.
        }
    }

    private static bool WaitForStableFile(string filePath)
    {
        long lastSize = -1;
        var stableReads = 0;
        var deadline = Environment.TickCount64 + 3000;

        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    stableReads = 0;
                    Thread.Sleep(50);
                    continue;
                }

                var size = new FileInfo(filePath).Length;
                if (size <= 0)
                {
                    stableReads = 0;
                    Thread.Sleep(50);
                    continue;
                }

                if (size == lastSize)
                {
                    stableReads++;
                    if (stableReads >= 2)
                        return true;
                }
                else
                {
                    stableReads = 0;
                    lastSize = size;
                }

                Thread.Sleep(50);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }

        return File.Exists(filePath) && new FileInfo(filePath).Length > 0;
    }
}
