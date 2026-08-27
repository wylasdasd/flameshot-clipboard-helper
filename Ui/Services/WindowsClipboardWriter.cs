using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using FlameshotClipboardHelper.Core;

namespace FlameshotClipboardHelper.Ui.Services;

/// <summary>
/// Windows shell clipboard (CF_HDROP + PNG + CF_DIB). Avoids Avalonia SetBitmap DIB/alpha issues.
/// </summary>
internal sealed class WindowsClipboardWriter : IClipboardWriter
{
    private const uint CfHdrop = 15;
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;

    private const long MaxBitmapFileBytes = 15 * 1024 * 1024;
    private const long MaxPixelCount = 3840L * 2160L;

    private static readonly uint PngFormat = RegisterClipboardFormat("PNG");

    public bool TryPushScreenshot(string filePath)
    {
        filePath = Path.GetFullPath(filePath);

        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            if (!TrySetFileDrop(filePath))
                return false;

            TrySetPng(filePath);
            TrySetDib(filePath);
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TrySetFileDrop(string filePath)
    {
        var hDrop = CreateHDrop([filePath]);
        if (hDrop == IntPtr.Zero)
            return false;

        return SetClipboardData(CfHdrop, hDrop) != IntPtr.Zero;
    }

    private static void TrySetPng(string filePath)
    {
        var length = new FileInfo(filePath).Length;
        if (length <= 0 || length > MaxBitmapFileBytes)
            return;

        byte[] pngBytes;
        try
        {
            pngBytes = File.ReadAllBytes(filePath);
        }
        catch
        {
            return;
        }

        var hMem = AllocBytes(pngBytes);
        if (hMem == IntPtr.Zero)
            return;

        SetClipboardData(PngFormat, hMem);
    }

    private static void TrySetDib(string filePath)
    {
        var length = new FileInfo(filePath).Length;
        if (length <= 0 || length > MaxBitmapFileBytes)
            return;

        try
        {
            using var img = Image.FromFile(filePath);
            if ((long)img.Width * img.Height > MaxPixelCount)
                return;

            using var flat = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(flat))
            {
                g.Clear(Color.White);
                g.DrawImage(img, 0, 0, img.Width, img.Height);
            }

            var hDib = CreateDib(flat);
            if (hDib != IntPtr.Zero)
                SetClipboardData(CfDib, hDib);
        }
        catch (OutOfMemoryException)
        {
            // File reference and PNG alone are still usable.
        }
        catch (ArgumentException)
        {
            // PNG not fully written yet; retry loop handles that.
        }
    }

    private static IntPtr CreateHDrop(IReadOnlyList<string> files)
    {
        var sb = new StringBuilder();
        foreach (var file in files)
            sb.Append(file).Append('\0');
        sb.Append('\0');

        var fileListBytes = Encoding.Unicode.GetBytes(sb.ToString());
        var headerSize = Marshal.SizeOf<DropFiles>();
        var totalSize = headerSize + fileListBytes.Length;

        var hGlobal = GlobalAlloc(GmemMoveable, (nuint)totalSize);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var locked = GlobalLock(hGlobal);
        if (locked == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            var drop = new DropFiles
            {
                pFiles = (uint)headerSize,
                fWide = 1,
            };
            Marshal.StructureToPtr(drop, locked, false);
            Marshal.Copy(fileListBytes, 0, locked + headerSize, fileListBytes.Length);
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    private static IntPtr CreateDib(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = bmpData.Stride;
            var imageSize = stride * bitmap.Height;
            var headerSize = Marshal.SizeOf<BitmapInfoHeader>();
            var totalSize = headerSize + imageSize;

            var hGlobal = GlobalAlloc(GmemMoveable, (nuint)totalSize);
            if (hGlobal == IntPtr.Zero)
                return IntPtr.Zero;

            var locked = GlobalLock(hGlobal);
            if (locked == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                var header = new BitmapInfoHeader
                {
                    biSize = (uint)headerSize,
                    biWidth = bitmap.Width,
                    biHeight = bitmap.Height,
                    biPlanes = 1,
                    biBitCount = 24,
                    biCompression = 0,
                    biSizeImage = (uint)imageSize,
                };
                Marshal.StructureToPtr(header, locked, false);

                var dest = locked + headerSize;
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var srcRow = bmpData.Scan0 + y * stride;
                    var destRow = dest + (bitmap.Height - 1 - y) * stride;
                    CopyMemory(destRow, srcRow, (uint)stride);
                }
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            return hGlobal;
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }
    }

    private static IntPtr AllocBytes(byte[] bytes)
    {
        var hGlobal = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (hGlobal == IntPtr.Zero)
            return IntPtr.Zero;

        var locked = GlobalLock(hGlobal);
        if (locked == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

    [StructLayout(LayoutKind.Sequential)]
    private struct DropFiles
    {
        public uint pFiles;
        public int ptX;
        public int ptY;
        public int fNC;
        public uint fWide;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }
}
