namespace BrowserTools;

/// <summary>Wire values frozen against the 2.0 Chrome extension.</summary>
public static class Constants
{
    public const string ServerName = "BrowserTools Connector";
    public const string ServerSignature = "mcp-browser-connector-24x7";
    public const string ServerVersion = "2.0.0";
    public const string McpServerName = "browser-tools-mcp";

    public const int DefaultPort = 3025;
    public const int PortRangeEnd = 3035;

    public const string IdentityPath = "/.identity";
    public const string ExtensionWebSocketPath = "/extension-ws";
    public const string ApiPrefix = "/api";

    public const string SessionDirectoryName = ".browser-tools-mcp";
    public const string SessionFileName = "session.json";
    public const string LoopbackHost = "127.0.0.1";
}
