using System.Runtime.InteropServices;

namespace FlameshotClipboardHelper.Core;

internal static class ScreenshotClipboardService
{
    private static readonly object Gate = new();

    public static bool TryPush(string filePath, IClipboardWriter writer)
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
                    if (writer.TryPushScreenshot(filePath))
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
        }

        return false;
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
