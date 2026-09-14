using System.Text.Json;

namespace BrowserTools;

public sealed class ConsoleEntry
{
    public string Type { get; init; } = "console-log";
    public string Level { get; init; } = "log";
    public string Message { get; init; } = "";
    public long Timestamp { get; init; }
    public string? TabId { get; init; }
    public string? Url { get; init; }
    public JsonElement? StackTrace { get; init; }
}

public sealed class NetworkEntry
{
    public string Type { get; init; } = "network-request";
    public string Url { get; init; } = "";
    public string Method { get; init; } = "GET";
    public int Status { get; init; }
    public long Timestamp { get; init; }
    public long? StartedAt { get; init; }
    public string? TabId { get; init; }
    public int? DurationMs { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, string>? RequestHeaders { get; init; }
    public Dictionary<string, string>? ResponseHeaders { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
}

public readonly record struct QueryResult<T>(IReadOnlyList<T> Entries, int Total, int Returned, bool Truncated);

public sealed class ConsoleQuery
{
    public bool ErrorsOnly { get; init; }
    public IReadOnlyList<string>? Keywords { get; init; }
    public string? TabId { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
    public bool AllTabs { get; init; }
}

public sealed class NetworkQuery
{
    public bool ErrorsOnly { get; init; }
    public IReadOnlyList<string>? UrlKeywords { get; init; }
    public IReadOnlyList<string>? BodyKeywords { get; init; }
    public string? TabId { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
    public bool AllTabs { get; init; }
}

public readonly record struct PageState(string Url, string? TabId);
