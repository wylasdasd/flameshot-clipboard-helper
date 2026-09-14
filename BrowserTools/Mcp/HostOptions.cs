namespace BrowserTools.Mcp;

/// <summary>Parsed CLI / environment. Host maps argv; this type lives in the class library.</summary>
public sealed class HostOptions
{
    public bool ShowVersion { get; init; }
    public bool ShowHelp { get; init; }
    public bool Doctor { get; init; }
    public int? Port { get; init; }
    public string? Host { get; init; }
    public string? ScreenshotDir { get; init; }
    public string? ConnectUrl { get; init; }
    public string? Token { get; init; }
    public bool Redact { get; init; } = true;
    public bool Verbose { get; init; }
}
