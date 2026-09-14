namespace BrowserTools;

/// <summary>Stderr only — stdout is reserved for MCP JSON-RPC.</summary>
public static class Log
{
    public static void Info(string scope, string message)
        => Console.Error.WriteLine($"{Stamp()} [{scope}] {message}");

    public static void Warn(string scope, string message)
        => Console.Error.WriteLine($"{Stamp()} [{scope}] {message}");

    public static void Error(string scope, string message)
        => Console.Error.WriteLine($"{Stamp()} [{scope}] {message}");

    public static string Clip(string value, int limit = 160)
    {
        var flat = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > limit ? flat[..limit] + "…" : flat;
    }

    private static string Stamp() => DateTimeOffset.Now.ToString("HH:mm:ss");
}
