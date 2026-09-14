using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public static class McpStdioServer
{
    public static async Task RunAsync(IConnectorClient client, CancellationToken cancellationToken)
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        var reader = new StreamReader(input, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await ReadMessageAsync(reader, cancellationToken);
            if (payload is null)
                break;

            JsonElement envelope;
            try
            {
                envelope = JsonUtil.Parse(payload);
            }
            catch
            {
                Log.Warn("mcp", "Discarded unparseable JSON-RPC frame");
                continue;
            }

            var method = JsonUtil.GetString(envelope, "method");
            JsonUtil.TryGet(envelope, "id", out var id);
            var hasId = envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("id", out _);
            JsonUtil.TryGet(envelope, "params", out var @params);

            if (!hasId)
            {
                // notification
                continue;
            }

            try
            {
                var result = await DispatchAsync(client, method, @params);
                await WriteResultAsync(output, id, result, cancellationToken);
            }
            catch (Exception ex)
            {
                await WriteErrorAsync(output, id, -32000, ex.Message, cancellationToken);
            }
        }
    }

    private static async Task<JsonNode> DispatchAsync(IConnectorClient client, string method, JsonElement @params)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject(),
                        ["resources"] = new JsonObject { ["subscribe"] = false },
                        ["prompts"] = new JsonObject()
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = Constants.McpServerName,
                        ["version"] = Constants.ServerVersion
                    }
                };
            case "ping":
                return new JsonObject();
            case "tools/list":
                return new JsonObject { ["tools"] = ToolRouter.ListTools() };
            case "tools/call":
            {
                var name = JsonUtil.GetString(@params, "name");
                var args = JsonUtil.TryGet(@params, "arguments", out var a) ? a : JsonUtil.Parse("{}");
                return await ToolRouter.CallAsync(client, name, args);
            }
            case "resources/list":
                return new JsonObject { ["resources"] = new JsonArray() };
            case "resources/templates/list":
                return new JsonObject { ["resourceTemplates"] = ToolRouter.ListResourceTemplates() };
            case "resources/read":
            {
                var uri = JsonUtil.GetString(@params, "uri");
                return await ToolRouter.ReadResourceAsync(client, uri)
                       ?? throw new InvalidOperationException($"Unknown resource: {uri}");
            }
            case "prompts/list":
                return new JsonObject { ["prompts"] = Prompts.List() };
            case "prompts/get":
            {
                var name = JsonUtil.GetString(@params, "name");
                var text = Prompts.GetText(name) ?? throw new InvalidOperationException($"Unknown prompt: {name}");
                var messages = new JsonArray();
                JsonUtil.Push(messages, new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonObject { ["type"] = "text", ["text"] = text }
                });
                return new JsonObject
                {
                    ["description"] = name,
                    ["messages"] = messages
                };
            }
            default:
                throw new InvalidOperationException($"Method not found: {method}");
        }
    }

    private static async Task<string?> ReadMessageAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var first = await ReadLineAsync(reader, cancellationToken);
        if (first is null)
            return null;

        if (first.StartsWith('{'))
            return first;

        var contentLength = 0;
        var line = first;
        while (!string.IsNullOrEmpty(line))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), out var n))
                contentLength = n;
            line = await ReadLineAsync(reader, cancellationToken);
            if (line is null)
                return null;
        }

        if (contentLength <= 0)
            return await ReadMessageAsync(reader, cancellationToken);

        var buffer = new char[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var n = await reader.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken);
            if (n == 0)
                return null;
            read += n;
        }

        return new string(buffer);
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        return line;
    }

    private static async Task WriteResultAsync(Stream output, JsonElement id, JsonNode result, CancellationToken cancellationToken)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = IdNode(id),
            ["result"] = result
        };
        await WriteFrameAsync(output, envelope, cancellationToken);
    }

    private static async Task WriteErrorAsync(Stream output, JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = IdNode(id),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        await WriteFrameAsync(output, envelope, cancellationToken);
    }

    private static JsonNode? IdNode(JsonElement id)
        => id.ValueKind switch
        {
            JsonValueKind.Number => JsonValue.Create(id.GetInt64()),
            JsonValueKind.String => JsonValue.Create(id.GetString()),
            JsonValueKind.Null => null,
            _ => JsonNode.Parse(id.GetRawText())
        };

    private static async Task WriteFrameAsync(Stream output, JsonNode envelope, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, McpJsonContext.Default.JsonNode);
        var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(bytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
