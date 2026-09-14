using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BrowserTools.Mcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IdentityPayload))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonRpcEnvelope))]
internal sealed partial class McpJsonContext : JsonSerializerContext;

public sealed class JsonRpcEnvelope
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonElement? Id { get; set; }
    public string? Method { get; set; }
    public JsonElement? Params { get; set; }
    public JsonElement? Result { get; set; }
    public JsonElement? Error { get; set; }
}
