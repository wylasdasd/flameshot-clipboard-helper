using System.Text.Json.Serialization;

namespace FlameshotClipboardHelper.Core;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext;
