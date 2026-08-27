using Microsoft.Win32;

namespace FlameshotClipboardHelper.Core;

internal static class StartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FlameshotClipboardHelper";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open Run registry key.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve executable path.");
        key.SetValue(ValueName, $"\"{exe}\"");
    }
}
