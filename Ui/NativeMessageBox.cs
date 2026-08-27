using System.Runtime.InteropServices;

namespace FlameshotClipboardHelper.Ui;

internal static class NativeMessageBox
{
    private const uint MbIconInformation = 0x0000_0040;

    public static void ShowInfo(string text, string caption)
        => _ = MessageBoxW(IntPtr.Zero, text, caption, MbIconInformation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
