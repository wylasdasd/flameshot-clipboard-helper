using FlameshotClipboardHelper.Tray;

namespace FlameshotClipboardHelper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!SingleInstance.TryBecomePrimary())
            return;

        Application.Run(new TrayApplicationContext());
    }
}
