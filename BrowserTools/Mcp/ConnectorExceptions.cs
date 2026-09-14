namespace BrowserTools.Mcp;

public sealed class NoExtensionException : Exception
{
    public NoExtensionException()
        : base(
            "No browser extension is connected. Open Chrome DevTools (F12) on the page you want to inspect; " +
            "capture starts as soon as DevTools is open. Run `browser-tools-mcp --doctor` to check the setup.")
    {
    }
}

public sealed class UnknownTabException : Exception
{
    public UnknownTabException(string message) : base(message) { }
}

public sealed class ExtensionRequestException : Exception
{
    public ExtensionRequestException(string message) : base(message) { }
}

public sealed class ExtensionTimeoutException : Exception
{
    public ExtensionTimeoutException(string message) : base(message) { }
}

public sealed class AuditUnavailableException : Exception
{
    public AuditUnavailableException()
        : base("Lighthouse audits are not available in this native build. Pass a url to an external Lighthouse run if you need scores.")
    {
    }
}
