using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BrowserTools;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CaptureSettings))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(ConsoleEntry))]
[JsonSerializable(typeof(NetworkEntry))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class BrowserToolsJsonContext : JsonSerializerContext;
