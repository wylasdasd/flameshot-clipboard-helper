using System.Text.RegularExpressions;

namespace BrowserTools;

public static partial class Security
{
    private static readonly HashSet<string> AllowedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1", "localhost", "::1", "[::1]"
    };

    private static readonly HashSet<string> LoopbackHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1", "localhost", "::1"
    };

    [GeneratedRegex(@"^(chrome-extension|moz-extension|safari-web-extension|extension)://[A-Za-z0-9._-]+$")]
    private static partial Regex ExtensionOrigin();

    public static bool IsLoopbackHost(string host) => LoopbackHosts.Contains(host);

    public static bool IsExtensionOrigin(string? origin)
        => !string.IsNullOrEmpty(origin) && ExtensionOrigin().IsMatch(origin);

    public static string HostnameOf(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader))
            return "";
        if (hostHeader.StartsWith('['))
        {
            var end = hostHeader.IndexOf(']');
            return end == -1 ? hostHeader : hostHeader[..(end + 1)];
        }

        var colon = hostHeader.LastIndexOf(':');
        return colon == -1 ? hostHeader : hostHeader[..colon];
    }

    public static bool IsAllowedHost(string? hostHeader)
        => AllowedHostnames.Contains(HostnameOf(hostHeader));

    public static bool IsAllowedHttpOrigin(string? origin)
        => string.IsNullOrEmpty(origin) || IsExtensionOrigin(origin);
}
