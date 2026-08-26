using System.Reflection;

namespace FlameshotClipboardHelper;

internal static class AppInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
