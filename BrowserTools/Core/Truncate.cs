using System.Text.Json;

namespace BrowserTools;

public static class Truncate
{
    public const string Suffix = "... (truncated)";

    public static JsonElement StringsInData(JsonElement data, int maxLength)
        => Walk(data, maxLength);

    public static string StringsInData(string data, int maxLength)
        => data.Length > maxLength ? data[..maxLength] + Suffix : data;

    public static List<T> SelectLogsWithinBudget<T>(IReadOnlyList<T> logs, int budget, Func<T, int> sizeOf, Func<T, T> shrink)
    {
        if (logs.Count == 0)
            return [];

        var selected = new List<T>();
        var used = 0;

        for (var i = logs.Count - 1; i >= 0; i--)
        {
            var entry = logs[i];
            var size = sizeOf(entry);
            if (used + size > budget)
            {
                if (selected.Count == 0)
                    selected.Add(shrink(entry));
                break;
            }

            selected.Add(entry);
            used += size;
        }

        selected.Reverse();
        return selected;
    }

    private static JsonElement Walk(JsonElement data, int maxLength)
    {
        return data.ValueKind switch
        {
            JsonValueKind.String => JsonUtil.FromString(StringsInData(data.GetString() ?? "", maxLength)),
            JsonValueKind.Array => WalkArray(data, maxLength),
            JsonValueKind.Object => WalkObject(data, maxLength),
            _ => data.Clone()
        };
    }

    private static JsonElement WalkArray(JsonElement array, int maxLength)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in array.EnumerateArray())
                Walk(item, maxLength).WriteTo(writer);
            writer.WriteEndArray();
        }

        return JsonUtil.Parse(stream.ToArray());
    }

    private static JsonElement WalkObject(JsonElement obj, int maxLength)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in obj.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                Walk(property.Value, maxLength).WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonUtil.Parse(stream.ToArray());
    }
}
