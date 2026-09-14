using System.Text.RegularExpressions;

namespace BrowserTools;

public readonly record struct ParsedImage(string MimeType, string Base64);

public static partial class ImageData
{
    [GeneratedRegex(@"^data:([^;,]+);base64,(.*)$", RegexOptions.Singleline)]
    private static partial Regex DataUrl();

    public static ParsedImage? ParseDataUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var match = DataUrl().Match(value);
        if (!match.Success)
            return value.StartsWith("data:", StringComparison.Ordinal)
                ? null
                : new ParsedImage("image/png", value);

        var mime = match.Groups[1].Value.ToLowerInvariant();
        var base64 = match.Groups[2].Value;
        if (mime is not ("image/png" or "image/jpeg") || base64.Length == 0)
            return null;
        return new ParsedImage(mime, base64);
    }

    public static string ExtensionForMime(string mimeType)
        => mimeType == "image/jpeg" ? "jpg" : "png";

    public static int ApproximateBytes(string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return 0;
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2
            : base64.EndsWith('=') ? 1 : 0;
        return Math.Max(0, base64.Length * 3 / 4 - padding);
    }
}
