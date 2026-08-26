using FlameshotClipboardHelper.Tray;

namespace FlameshotClipboardHelper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!SingleInstance.TryBecomePrimary())
        {
            if (!SingleInstance.TrySignalOpenSettings())
                SingleInstance.ShowAlreadyRunningMessage();
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
