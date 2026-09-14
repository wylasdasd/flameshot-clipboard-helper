namespace BrowserTools;

/// <summary>Published so another MCP process can attach without scanning the network.</summary>
public sealed class SessionInfo
{
    public int Port { get; init; }
    public string Token { get; init; } = "";
    public int Pid { get; init; }
    public string StartedAt { get; init; } = "";
    public string Version { get; init; } = "";
}
