using System.Reflection;

namespace FlameshotClipboardHelper.Core;

internal static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
}
