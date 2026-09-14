using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public sealed class HttpConnectorClient : IConnectorClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public HttpConnectorClient(string baseUrl, string token)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(70) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void Dispose() => _http.Dispose();

    public async Task<TabScopedResult<ConsoleEntry>> ConsoleAsync(ConsoleQuery query)
    {
        var node = await RequestAsync("/api/console", HttpMethod.Get, null, ConsoleQueryPairs(query));
        return ParseScoped(node, ParseConsoleEntry);
    }

    public async Task<TabScopedResult<NetworkEntry>> NetworkAsync(NetworkQuery query)
    {
        var node = await RequestAsync("/api/network", HttpMethod.Get, null, NetworkQueryPairs(query));
        return ParseScoped(node, ParseNetworkEntry);
    }

    public async Task<JsonElement?> SelectedElementAsync(string? tabId)
    {
        var node = await RequestAsync("/api/selected-element", HttpMethod.Get, null,
            [new KeyValuePair<string, string?>("tabId", tabId)]);
        var element = node["element"];
        if (element is null || element.GetValueKind() == JsonValueKind.Null)
            return null;
        return JsonUtil.Parse(JsonUtil.Serialize(element));
    }

    public Task<JsonObject> PageAsync() => RequestAsync("/api/page", HttpMethod.Get, null, null);

    public Task<JsonObject> StatusAsync() => RequestAsync("/api/status", HttpMethod.Get, null, null);

    public Task<JsonObject> TabsAsync() => RequestAsync("/api/tabs", HttpMethod.Get, null, null);

    public async Task WipeAsync(string? tabId)
        => await RequestAsync("/api/wipe", HttpMethod.Post, TabBody(tabId), null);

    public async Task<ScreenshotCapture> ScreenshotAsync(string? name, string? tabId)
    {
        var body = TabBody(tabId);
        if (name is not null)
            body["name"] = name;
        var node = await RequestAsync("/api/screenshot", HttpMethod.Post, body, null);
        return new ScreenshotCapture(
            Str(node, "path"),
            Str(node, "data"),
            Str(node, "name"),
            Str(node, "mimeType") is { Length: > 0 } mime ? mime : "image/png",
            node["bytes"]?.GetValue<int>() ?? 0,
            node["withinBudget"]?.GetValue<bool>() ?? false,
            TabIdOf(node["tabId"]),
            Str(node, "url"));
    }

    public async Task RefreshAsync(string? tabId)
        => await RequestAsync("/api/refresh", HttpMethod.Post, TabBody(tabId), null);

    public async Task<JsonElement> StorageAsync(IReadOnlyList<string> kinds, string? tabId)
    {
        var arr = new JsonArray();
        foreach (var kind in kinds)
            JsonUtil.Push(arr, kind);
        var body = TabBody(tabId);
        body["kinds"] = arr;
        var node = await RequestAsync("/api/storage", HttpMethod.Post, body, null);
        return JsonUtil.Parse(JsonUtil.Serialize(node["storage"] ?? new JsonObject()));
    }

    public async Task<PageScriptResult> RunPageScriptAsync(string script, string? tabId, int? timeoutMs)
    {
        var body = TabBody(tabId);
        body["script"] = script;
        if (timeoutMs is int ms)
            body["timeoutMs"] = ms;
        var node = await RequestAsync("/api/script", HttpMethod.Post, body, null);
        var resultNode = node["result"] ?? JsonValue.Create((string?)null);
        return new PageScriptResult(
            JsonUtil.Parse(JsonUtil.Serialize(resultNode)),
            Str(node, "resultType"),
            node["awaited"]?.GetValue<bool>() ?? false,
            node["truncated"]?.GetValue<bool>() ?? false,
            TabIdOf(node["tabId"]),
            Str(node, "url"),
            node["otherTabs"]?.GetValue<int>() ?? 0);
    }

    public async Task<InteractResult> InteractAsync(InteractRequest request)
    {
        var body = TabBody(request.TabId);
        body["action"] = request.Action;
        if (request.Selector is not null) body["selector"] = request.Selector;
        if (request.Text is not null) body["text"] = request.Text;
        if (request.Key is not null) body["key"] = request.Key;
        if (request.X is not null) body["x"] = request.X;
        if (request.Y is not null) body["y"] = request.Y;
        if (request.DeltaX is not null) body["deltaX"] = request.DeltaX;
        if (request.DeltaY is not null) body["deltaY"] = request.DeltaY;
        var node = await RequestAsync("/api/interact", HttpMethod.Post, body, null);
        return new InteractResult(
            Str(node, "action"),
            node["matched"]?.GetValue<int>() ?? 0,
            node["x"]?.GetValue<double>(),
            node["y"]?.GetValue<double>(),
            EmptyToNull(Str(node, "tagName")),
            TabIdOf(node["tabId"]),
            Str(node, "url"),
            node["otherTabs"]?.GetValue<int>() ?? 0);
    }

    public async Task<ExportBundle<ConsoleEntry>> ExportConsoleAsync(string? tabId, bool allTabs)
    {
        var node = await RequestAsync("/api/export/console", HttpMethod.Get, null, ExportPairs(tabId, allTabs));
        return ParseExport(node, ParseConsoleEntry);
    }

    public async Task<ExportBundle<NetworkEntry>> ExportNetworkAsync(string? tabId, bool allTabs)
    {
        var node = await RequestAsync("/api/export/network", HttpMethod.Get, null, ExportPairs(tabId, allTabs));
        return ParseExport(node, ParseNetworkEntry);
    }

    public async Task<Artifact> ReadArtifactAsync(string kind, string name)
    {
        var node = await RequestAsync($"/api/artifact/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(name)}", HttpMethod.Get, null, null);
        return new Artifact(
            Str(node, "mimeType") is { Length: > 0 } mime ? mime : "application/octet-stream",
            node["text"]?.GetValue<string>(),
            node["blob"]?.GetValue<string>());
    }

    public async Task<JsonObject> RequestAsync(
        string path,
        HttpMethod method,
        JsonNode? body,
        IEnumerable<KeyValuePair<string, string?>>? query)
    {
        var url = new StringBuilder(_baseUrl).Append(path);
        if (query is not null)
        {
            var first = true;
            foreach (var (key, value) in query)
            {
                if (value is null)
                    continue;
                url.Append(first ? '?' : '&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        using var request = new HttpRequestMessage(method, url.ToString());
        if (body is not null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, McpJsonContext.Default.JsonNode);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var node = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        if (!response.IsSuccessStatusCode)
        {
            var message = node["error"]?.GetValue<string>() ?? $"Request failed ({(int)response.StatusCode})";
            throw new InvalidOperationException(message);
        }

        return node;
    }

    private static IEnumerable<KeyValuePair<string, string?>> ConsoleQueryPairs(ConsoleQuery query)
    {
        yield return new("errorsOnly", query.ErrorsOnly ? "true" : null);
        yield return new("keywords", Join(query.Keywords));
        yield return new("tabId", query.TabId);
        yield return new("allTabs", query.AllTabs ? "true" : null);
        yield return new("limit", query.Limit?.ToString());
        yield return new("offset", query.Offset?.ToString());
    }

    private static IEnumerable<KeyValuePair<string, string?>> NetworkQueryPairs(NetworkQuery query)
    {
        yield return new("errorsOnly", query.ErrorsOnly ? "true" : null);
        yield return new("urlKeywords", Join(query.UrlKeywords));
        yield return new("bodyKeywords", Join(query.BodyKeywords));
        yield return new("tabId", query.TabId);
        yield return new("allTabs", query.AllTabs ? "true" : null);
        yield return new("limit", query.Limit?.ToString());
        yield return new("offset", query.Offset?.ToString());
    }

    private static IEnumerable<KeyValuePair<string, string?>> ExportPairs(string? tabId, bool allTabs)
    {
        yield return new("tabId", tabId);
        yield return new("allTabs", allTabs ? "true" : null);
    }

    private static string? Join(IReadOnlyList<string>? items)
        => items is { Count: > 0 } ? string.Join(',', items) : null;

    private static JsonObject TabBody(string? tabId)
    {
        var body = new JsonObject();
        if (tabId is not null)
            body["tabId"] = JsonUtil.TabIdNode(tabId);
        return body;
    }

    private static TabScopedResult<T> ParseScoped<T>(JsonObject node, Func<JsonNode, T> parse)
    {
        var entries = new List<T>();
        if (node["entries"] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                    entries.Add(parse(item));
            }
        }

        return new TabScopedResult<T>(
            entries,
            node["total"]?.GetValue<int>() ?? entries.Count,
            node["returned"]?.GetValue<int>() ?? entries.Count,
            node["truncated"]?.GetValue<bool>() ?? false,
            TabIdOf(node["tabId"]),
            Str(node, "url"),
            node["otherTabs"]?.GetValue<int>() ?? 0);
    }

    private static ExportBundle<T> ParseExport<T>(JsonObject node, Func<JsonNode, T> parse)
    {
        var entries = new List<T>();
        if (node["entries"] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                    entries.Add(parse(item));
            }
        }

        return new ExportBundle<T>(TabIdOf(node["tabId"]), Str(node, "url"), entries);
    }

    private static ConsoleEntry ParseConsoleEntry(JsonNode node)
    {
        var obj = node.AsObject();
        JsonElement? stack = null;
        if (obj["stackTrace"] is { } st && st.GetValueKind() != JsonValueKind.Null)
            stack = JsonUtil.Parse(JsonUtil.Serialize(st));
        return new ConsoleEntry
        {
            Type = Str(obj, "type") is { Length: > 0 } type ? type : "console-log",
            Level = Str(obj, "level") is { Length: > 0 } level ? level : "log",
            Message = Str(obj, "message"),
            Timestamp = obj["timestamp"]?.GetValue<long>() ?? 0,
            TabId = TabIdOf(obj["tabId"]),
            Url = EmptyToNull(Str(obj, "url")),
            StackTrace = stack
        };
    }

    private static NetworkEntry ParseNetworkEntry(JsonNode node)
    {
        var obj = node.AsObject();
        return new NetworkEntry
        {
            Type = Str(obj, "type") is { Length: > 0 } type ? type : "network-request",
            Url = Str(obj, "url"),
            Method = Str(obj, "method") is { Length: > 0 } method ? method : "GET",
            Status = obj["status"]?.GetValue<int>() ?? 0,
            Timestamp = obj["timestamp"]?.GetValue<long>() ?? 0,
            StartedAt = obj["startedAt"]?.GetValue<long>(),
            TabId = TabIdOf(obj["tabId"]),
            DurationMs = obj["durationMs"]?.GetValue<int>(),
            Error = EmptyToNull(Str(obj, "error")),
            RequestHeaders = Headers(obj["requestHeaders"]),
            ResponseHeaders = Headers(obj["responseHeaders"]),
            RequestBody = EmptyToNull(Str(obj, "requestBody")),
            ResponseBody = EmptyToNull(Str(obj, "responseBody"))
        };
    }

    private static Dictionary<string, string>? Headers(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in obj)
        {
            headers[property.Key] = property.Value switch
            {
                null => "",
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                _ => JsonUtil.Serialize(property.Value)
            };
        }
        return headers;
    }

    private static string Str(JsonObject obj, string name)
        => obj[name]?.GetValue<string>() ?? "";

    private static string? TabIdOf(JsonNode? node)
    {
        if (node is null || node.GetValueKind() is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (node.GetValueKind() == JsonValueKind.Number)
            return node.GetValue<long>().ToString();
        var text = node.GetValue<string>();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
}
