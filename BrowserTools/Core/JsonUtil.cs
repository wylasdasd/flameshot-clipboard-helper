using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserTools;

public static class JsonUtil
{
    public static JsonElement Parse(ReadOnlySpan<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8.ToArray());
        return document.RootElement.Clone();
    }

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement FromString(string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            writer.WriteStringValue(value);
        return Parse(stream.ToArray());
    }

    public static JsonElement Null { get; } = Parse("null");

    public static string GetString(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object
           && obj.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    public static double GetNumber(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n))
            return n;
        return 0;
    }

    public static long GetInt64(JsonElement obj, string name)
        => (long)GetNumber(obj, name);

    public static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    public static string? TabIdString(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value))
            return null;
        return TabIdString(value);
    }

    public static string? TabIdString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n.ToString(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };

    public static JsonNode? TabIdNode(string? tabId)
    {
        if (tabId is null)
            return null;
        return long.TryParse(tabId, out var n) ? JsonValue.Create(n) : JsonValue.Create(tabId);
    }

    public static string CoerceMessage(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Undefined or JsonValueKind.Null => "",
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => value.ToString()
        };

    public static int JsonSize(JsonElement value)
    {
        try
        {
            return Encoding.UTF8.GetByteCount(value.GetRawText());
        }
        catch
        {
            return 0;
        }
    }

    public static int JsonSize(string json)
        => Encoding.UTF8.GetByteCount(json);

    public static JsonElement ObjectOrNull(object? _) => Null;

    public static void Push(JsonArray array, JsonNode? node) => array.Add(node);

    public static void Push(JsonArray array, string? value)
        => array.Add((JsonNode?)JsonValue.Create(value));

    public static string Serialize(JsonNode? node)
        => JsonSerializer.Serialize(node, BrowserToolsJsonContext.Default.JsonNode);
}
