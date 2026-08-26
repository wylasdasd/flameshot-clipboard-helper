using FlameshotClipboardHelper.Tray;

namespace FlameshotClipboardHelper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
