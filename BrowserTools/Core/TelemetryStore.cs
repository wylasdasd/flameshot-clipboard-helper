using System.Text.Json;

namespace BrowserTools;

public sealed class TelemetryStore
{
    public const string Unattributed = "__no-tab__";
    private static readonly HashSet<string> ErrorLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "assert", "critical"
    };

    private readonly bool _redact;
    private readonly List<ConsoleEntry> _console = [];
    private readonly List<NetworkEntry> _network = [];
    private readonly Dictionary<string, JsonElement> _selectedElements = new(StringComparer.Ordinal);
    private string _lastSelectedTab = Unattributed;
    private PageState _page = new("", null);

    public TelemetryStore(bool redact = true, CaptureSettings? settings = null)
    {
        _redact = redact;
        Settings = settings is null ? CaptureSettings.Defaults : CaptureSettings.Clone(settings);
    }

    public CaptureSettings Settings { get; private set; }

    public CaptureSettings UpdateSettings(JsonElement patch)
    {
        Settings = CaptureSettings.Merge(Settings, patch);
        EvictAll();
        return Settings;
    }

    public ConsoleEntry? AddConsole(JsonElement raw, string? connectionTabId)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            return null;

        var type = JsonUtil.GetString(raw, "type");
        if (string.IsNullOrEmpty(type))
            type = "console-log";
        var level = JsonUtil.GetString(raw, "level");
        if (string.IsNullOrEmpty(level))
            level = InferLevel(type);

        JsonUtil.TryGet(raw, "message", out var messageEl);
        var entry = new ConsoleEntry
        {
            Type = type,
            Level = level,
            Message = Clean(JsonUtil.CoerceMessage(messageEl)),
            Timestamp = AsTimestamp(raw, "timestamp"),
            TabId = ResolveTabId(connectionTabId, raw),
            Url = JsonUtil.TryGet(raw, "url", out _) ? Clean(JsonUtil.GetString(raw, "url")) : null,
            StackTrace = JsonUtil.TryGet(raw, "stackTrace", out var stack)
                ? CleanValue(stack)
                : null
        };

        _console.Add(entry);
        EvictTab(_console, TabKey(entry.TabId), e => e.TabId);
        return entry;
    }

    public NetworkEntry? AddNetwork(JsonElement raw, string? connectionTabId)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            return null;

        var type = JsonUtil.GetString(raw, "type");
        var duration = JsonUtil.GetNumber(raw, "durationMs");
        if (duration == 0)
            duration = JsonUtil.GetNumber(raw, "duration");

        Dictionary<string, string>? requestHeaders = null;
        if (JsonUtil.TryGet(raw, "requestHeaders", out var reqH) && reqH.ValueKind == JsonValueKind.Object)
            requestHeaders = CleanHeaders(reqH);
        Dictionary<string, string>? responseHeaders = null;
        if (JsonUtil.TryGet(raw, "responseHeaders", out var resH) && resH.ValueKind == JsonValueKind.Object)
            responseHeaders = CleanHeaders(resH);

        var startedAt = JsonUtil.GetInt64(raw, "startedAt");
        var error = JsonUtil.GetString(raw, "error");

        var entry = new NetworkEntry
        {
            Type = string.IsNullOrEmpty(type) ? "network-request" : type,
            Url = Clean(JsonUtil.GetString(raw, "url")),
            Method = string.IsNullOrEmpty(JsonUtil.GetString(raw, "method")) ? "GET" : JsonUtil.GetString(raw, "method"),
            Status = (int)JsonUtil.GetNumber(raw, "status"),
            Timestamp = AsTimestamp(raw, "timestamp"),
            StartedAt = startedAt > 0 ? startedAt : null,
            TabId = ResolveTabId(connectionTabId, raw),
            DurationMs = duration > 0 ? (int)duration : null,
            Error = string.IsNullOrEmpty(error) ? null : Clean(error),
            RequestHeaders = requestHeaders,
            ResponseHeaders = responseHeaders,
            RequestBody = JsonUtil.TryGet(raw, "requestBody", out var reqB) && reqB.ValueKind is not JsonValueKind.Null
                ? Clean(JsonUtil.CoerceMessage(reqB))
                : null,
            ResponseBody = JsonUtil.TryGet(raw, "responseBody", out var resB) && resB.ValueKind is not JsonValueKind.Null
                ? Clean(JsonUtil.CoerceMessage(resB))
                : null
        };

        _network.Add(entry);
        EvictTab(_network, TabKey(entry.TabId), e => e.TabId);
        return entry;
    }

    public void SetSelectedElement(JsonElement element, string? tabId)
    {
        var key = TabKey(tabId);
        if (element.ValueKind != JsonValueKind.Object)
        {
            _selectedElements.Remove(key);
            return;
        }

        _selectedElements[key] = CleanValue(element);
        _lastSelectedTab = key;
    }

    public JsonElement? GetSelectedElement(string? tabId = null)
    {
        var key = tabId is null ? _lastSelectedTab : TabKey(tabId);
        return _selectedElements.TryGetValue(key, out var value) ? value : null;
    }

    public void SetCurrentPage(string? url, string? tabId)
    {
        if (!string.IsNullOrEmpty(url))
            _page = _page with { Url = url };
        if (tabId is not null)
            _page = _page with { TabId = tabId };
    }

    public PageState GetCurrentPage() => _page;

    public void Wipe(string? tabId = null)
    {
        if (tabId is null)
        {
            _console.Clear();
            _network.Clear();
            _selectedElements.Clear();
            _lastSelectedTab = Unattributed;
            return;
        }

        var key = TabKey(tabId);
        _console.RemoveAll(e => TabKey(e.TabId) == key);
        _network.RemoveAll(e => TabKey(e.TabId) == key);
        _selectedElements.Remove(key);
    }

    public QueryResult<ConsoleEntry> QueryConsole(ConsoleQuery query)
    {
        IEnumerable<ConsoleEntry> matched = _console;
        if (query.TabId is not null)
        {
            var key = TabKey(query.TabId);
            matched = matched.Where(e => TabKey(e.TabId) == key);
        }

        if (query.ErrorsOnly)
            matched = matched.Where(e => ErrorLevels.Contains(e.Level));
        if (query.Keywords is { Count: > 0 })
            matched = matched.Where(e => MatchesAny([e.Message], query.Keywords));

        return Paginate(ByTime(matched.ToList(), e => e.Timestamp), query.Limit, query.Offset, SerializeConsole);
    }

    public QueryResult<NetworkEntry> QueryNetwork(NetworkQuery query)
    {
        IEnumerable<NetworkEntry> matched = _network;
        if (query.TabId is not null)
        {
            var key = TabKey(query.TabId);
            matched = matched.Where(e => TabKey(e.TabId) == key);
        }

        if (query.ErrorsOnly)
            matched = matched.Where(IsNetworkError);
        if (query.UrlKeywords is { Count: > 0 })
            matched = matched.Where(e => MatchesAny([e.Url], query.UrlKeywords));
        if (query.BodyKeywords is { Count: > 0 })
            matched = matched.Where(e => MatchesAny([e.ResponseBody, e.RequestBody], query.BodyKeywords));

        var result = Paginate(ByTime(matched.ToList(), e => e.Timestamp), query.Limit, query.Offset, SerializeNetwork);
        return result with { Entries = result.Entries.Select(ApplyHeaderVisibility).ToList() };
    }

    public List<ConsoleEntry> AllConsole(string? tabId = null)
    {
        if (tabId is null)
            return [.. _console];
        var key = TabKey(tabId);
        return _console.Where(e => TabKey(e.TabId) == key).ToList();
    }

    public List<NetworkEntry> AllNetwork(string? tabId = null)
    {
        IEnumerable<NetworkEntry> entries = _network;
        if (tabId is not null)
        {
            var key = TabKey(tabId);
            entries = entries.Where(e => TabKey(e.TabId) == key);
        }

        return entries.Select(ApplyHeaderVisibility).ToList();
    }

    public static string TabKey(string? tabId)
        => tabId is null ? Unattributed : tabId;

    private QueryResult<T> Paginate<T>(List<T> matched, int? limit, int? offset, Func<T, string> serialize)
    {
        var total = matched.Count;
        IReadOnlyList<T> page = matched;
        if (limit is not null || offset is not null)
        {
            var skip = Math.Max(0, offset ?? 0);
            var take = Math.Max(0, limit ?? total);
            var end = total - skip;
            page = end <= 0 ? [] : matched.GetRange(Math.Max(0, end - take), Math.Min(take, Math.Max(0, end)));
        }

        var entries = Truncate.SelectLogsWithinBudget(
            page,
            Settings.QueryLimit,
            item => JsonUtil.JsonSize(serialize(item)),
            item => Shrink(item, serialize));

        return new QueryResult<T>(entries, total, entries.Count, entries.Count < total);
    }

    private T Shrink<T>(T item, Func<T, string> serialize)
    {
        var cap = Math.Max(50, Settings.QueryLimit / 2);
        var json = Truncate.StringsInData(JsonUtil.Parse(serialize(item)), cap);
        if (item is ConsoleEntry)
            return (T)(object)ParseConsole(json);
        if (item is NetworkEntry)
            return (T)(object)ParseNetwork(json);
        return item;
    }

    private static ConsoleEntry ParseConsole(JsonElement json) => new()
    {
        Type = JsonUtil.GetString(json, "type"),
        Level = JsonUtil.GetString(json, "level"),
        Message = JsonUtil.GetString(json, "message"),
        Timestamp = JsonUtil.GetInt64(json, "timestamp"),
        TabId = JsonUtil.TabIdString(json, "tabId"),
        Url = JsonUtil.GetString(json, "url")
    };

    private static NetworkEntry ParseNetwork(JsonElement json) => new()
    {
        Type = JsonUtil.GetString(json, "type"),
        Url = JsonUtil.GetString(json, "url"),
        Method = JsonUtil.GetString(json, "method"),
        Status = (int)JsonUtil.GetNumber(json, "status"),
        Timestamp = JsonUtil.GetInt64(json, "timestamp")
    };

    internal string SerializeConsole(ConsoleEntry entry)
        => JsonSerializer.Serialize(entry, BrowserToolsJsonContext.Default.ConsoleEntry);

    internal string SerializeNetwork(NetworkEntry entry)
        => JsonSerializer.Serialize(entry, BrowserToolsJsonContext.Default.NetworkEntry);

    private NetworkEntry ApplyHeaderVisibility(NetworkEntry entry)
    {
        if (Settings.ShowRequestHeaders && Settings.ShowResponseHeaders)
            return entry;
        return new NetworkEntry
        {
            Type = entry.Type,
            Url = entry.Url,
            Method = entry.Method,
            Status = entry.Status,
            Timestamp = entry.Timestamp,
            StartedAt = entry.StartedAt,
            TabId = entry.TabId,
            DurationMs = entry.DurationMs,
            Error = entry.Error,
            RequestHeaders = Settings.ShowRequestHeaders ? entry.RequestHeaders : null,
            ResponseHeaders = Settings.ShowResponseHeaders ? entry.ResponseHeaders : null,
            RequestBody = entry.RequestBody,
            ResponseBody = entry.ResponseBody
        };
    }

    private void EvictTab<T>(List<T> list, string key, Func<T, string?> tabOf)
    {
        var limit = Settings.LogLimit;
        var seen = 0;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (TabKey(tabOf(list[i])) != key)
                continue;
            seen += 1;
            if (seen > limit)
                list.RemoveAt(i);
        }
    }

    private void EvictAll()
    {
        var consoleKeys = _console.Select(e => TabKey(e.TabId)).ToHashSet(StringComparer.Ordinal);
        foreach (var key in consoleKeys)
            EvictTab(_console, key, e => e.TabId);
        var networkKeys = _network.Select(e => TabKey(e.TabId)).ToHashSet(StringComparer.Ordinal);
        foreach (var key in networkKeys)
            EvictTab(_network, key, e => e.TabId);
    }

    private Dictionary<string, string> CleanHeaders(JsonElement headers)
    {
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in headers.EnumerateObject())
            raw[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : property.Value.GetRawText();

        var redacted = _redact ? Redact.Headers(raw) : raw;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in redacted)
            result[name] = Truncate.StringsInData(value, Settings.StringSizeLimit);
        return result;
    }

    private string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var capped = value.Length > Settings.MaxLogSize ? value[..Settings.MaxLogSize] : value;
        var scrubbed = _redact ? Redact.SecretsInString(capped) : capped;
        return Truncate.StringsInData(scrubbed, Settings.StringSizeLimit);
    }

    private JsonElement CleanValue(JsonElement value)
    {
        var scrubbed = _redact ? Redact.Value(value) : value;
        return Truncate.StringsInData(scrubbed, Settings.StringSizeLimit);
    }

    private static string? ResolveTabId(string? explicitId, JsonElement raw)
        => explicitId ?? JsonUtil.TabIdString(raw, "tabId");

    private static bool IsNetworkError(NetworkEntry entry)
        => !string.IsNullOrEmpty(entry.Error) || entry.Status >= 400 || entry.Status == 0;

    private static bool MatchesAny(IEnumerable<string?> fields, IReadOnlyList<string> keywords)
    {
        var needles = keywords
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k.ToLowerInvariant())
            .ToList();
        if (needles.Count == 0)
            return true;

        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field))
                continue;
            var haystack = field.ToLowerInvariant();
            if (needles.Any(n => haystack.Contains(n)))
                return true;
        }

        return false;
    }

    private static List<T> ByTime<T>(List<T> entries, Func<T, long> timestamp)
        => [.. entries.OrderBy(timestamp)];

    private static long AsTimestamp(JsonElement raw, string name)
    {
        var n = JsonUtil.GetInt64(raw, name);
        if (n > 0)
            return n;
        if (JsonUtil.TryGet(raw, name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(value.GetString(), out var parsed))
                return parsed.ToUnixTimeMilliseconds();
        }

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static string InferLevel(string type)
    {
        if (type.Contains("error", StringComparison.OrdinalIgnoreCase))
            return "error";
        if (type.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return "warning";
        if (type.Contains("info", StringComparison.OrdinalIgnoreCase))
            return "info";
        return "log";
    }
}
