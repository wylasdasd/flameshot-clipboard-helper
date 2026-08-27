using Avalonia;
using FlameshotClipboardHelper.Core;
using FlameshotClipboardHelper.Ui;

namespace FlameshotClipboardHelper;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!SingleInstance.TryBecomePrimary())
        {
            if (!SingleInstance.TrySignalOpenSettings())
                NativeMessageBox.ShowInfo(SingleInstance.GetAlreadyRunningMessage(), L.AppTitle);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
