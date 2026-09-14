using System.Text.Json.Nodes;

namespace BrowserTools;

public static class HarBuilder
{
    public static JsonObject Build(IReadOnlyList<NetworkEntry> entries)
    {
        var harEntries = new JsonArray();
        foreach (var entry in entries)
        {
            var time = entry.DurationMs ?? 0;
            var responseBody = entry.ResponseBody ?? "";
            var request = new JsonObject
            {
                ["method"] = entry.Method ?? "GET",
                ["url"] = entry.Url ?? "",
                ["httpVersion"] = "HTTP/1.1",
                ["headers"] = HeaderList(entry.RequestHeaders),
                ["queryString"] = QueryString(entry.Url),
                ["cookies"] = new JsonArray(),
                ["headersSize"] = -1,
                ["bodySize"] = entry.RequestBody?.Length ?? 0
            };
            if (!string.IsNullOrEmpty(entry.RequestBody))
            {
                request["postData"] = new JsonObject
                {
                    ["mimeType"] = MimeTypeOf(entry.RequestHeaders) ?? "application/octet-stream",
                    ["text"] = entry.RequestBody
                };
            }

            var content = new JsonObject
            {
                ["size"] = responseBody.Length,
                ["mimeType"] = MimeTypeOf(entry.ResponseHeaders) ?? ""
            };
            if (responseBody.Length > 0)
                content["text"] = responseBody;

            JsonUtil.Push(harEntries, new JsonObject
            {
                ["startedDateTime"] = StartedDateTime(entry),
                ["time"] = time,
                ["request"] = request,
                ["response"] = new JsonObject
                {
                    ["status"] = entry.Status,
                    ["statusText"] = entry.Error ?? "",
                    ["httpVersion"] = "HTTP/1.1",
                    ["headers"] = HeaderList(entry.ResponseHeaders),
                    ["cookies"] = new JsonArray(),
                    ["content"] = content,
                    ["redirectURL"] = "",
                    ["headersSize"] = -1,
                    ["bodySize"] = responseBody.Length
                },
                ["cache"] = new JsonObject(),
                ["timings"] = new JsonObject { ["send"] = 0, ["wait"] = time, ["receive"] = 0 }
            });
        }

        return new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["version"] = "1.2",
                ["creator"] = new JsonObject
                {
                    ["name"] = "BrowserTools MCP",
                    ["version"] = Constants.ServerVersion
                },
                ["pages"] = new JsonArray(),
                ["entries"] = harEntries
            }
        };
    }

    private static JsonArray HeaderList(Dictionary<string, string>? headers)
    {
        var list = new JsonArray();
        if (headers is null)
            return list;
        foreach (var (name, value) in headers)
            JsonUtil.Push(list, new JsonObject { ["name"] = name, ["value"] = value });
        return list;
    }

    private static JsonArray QueryString(string? url)
    {
        var list = new JsonArray();
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return list;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            JsonUtil.Push(list, new JsonObject
            {
                ["name"] = Uri.UnescapeDataString(parts[0]),
                ["value"] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : ""
            });
        }

        return list;
    }

    private static string MimeTypeOf(Dictionary<string, string>? headers)
    {
        if (headers is null)
            return "";
        foreach (var (name, value) in headers)
        {
            if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                return value.Split(';')[0].Trim();
        }

        return "";
    }

    private static string StartedDateTime(NetworkEntry entry)
    {
        var finished = entry.Timestamp > 0 ? entry.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ms = finished;
        if (entry.StartedAt is > 0)
            ms = entry.StartedAt.Value;
        else if (entry.DurationMs is > 0)
            ms = finished - entry.DurationMs.Value;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToString("o");
    }
}
