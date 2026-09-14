namespace BrowserTools.Mcp;

/// <summary>JSON body of <c>GET /.identity</c>. Signature must match the extension.</summary>
public sealed class IdentityPayload
{
    public string Name { get; init; } = Constants.ServerName;
    public string Version { get; init; } = Constants.ServerVersion;
    public string Signature { get; init; } = Constants.ServerSignature;
    public int Port { get; init; }
    public bool ExtensionConnected { get; init; }
}
