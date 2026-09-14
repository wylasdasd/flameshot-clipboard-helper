using System.Text.Json;

namespace BrowserTools;

/// <summary>Allowlisted capture limits. Unknown or out-of-range patches are discarded.</summary>
public sealed class CaptureSettings
{
    public int LogLimit { get; init; } = Limits.LogLimit.Default;
    public int QueryLimit { get; init; } = Limits.QueryLimit.Default;
    public int StringSizeLimit { get; init; } = Limits.StringSizeLimit.Default;
    public int MaxLogSize { get; init; } = Limits.MaxLogSize.Default;
    public int ScreenshotMaxBytes { get; init; } = Limits.ScreenshotMaxBytes.Default;
    public bool ShowRequestHeaders { get; init; }
    public bool ShowResponseHeaders { get; init; }

    public static CaptureSettings Defaults { get; } = new();

    public static CaptureSettings Merge(CaptureSettings current, JsonElement patch)
    {
        if (patch.ValueKind is not JsonValueKind.Object)
            return Clone(current);

        return new CaptureSettings
        {
            LogLimit = ReadInt(patch, "logLimit", current.LogLimit, Limits.LogLimit),
            QueryLimit = ReadInt(patch, "queryLimit", current.QueryLimit, Limits.QueryLimit),
            StringSizeLimit = ReadInt(patch, "stringSizeLimit", current.StringSizeLimit, Limits.StringSizeLimit),
            MaxLogSize = ReadInt(patch, "maxLogSize", current.MaxLogSize, Limits.MaxLogSize),
            ScreenshotMaxBytes = ReadInt(patch, "screenshotMaxBytes", current.ScreenshotMaxBytes, Limits.ScreenshotMaxBytes),
            ShowRequestHeaders = ReadBool(patch, "showRequestHeaders", current.ShowRequestHeaders),
            ShowResponseHeaders = ReadBool(patch, "showResponseHeaders", current.ShowResponseHeaders)
        };
    }

    public static CaptureSettings Clone(CaptureSettings current) => new()
    {
        LogLimit = current.LogLimit,
        QueryLimit = current.QueryLimit,
        StringSizeLimit = current.StringSizeLimit,
        MaxLogSize = current.MaxLogSize,
        ScreenshotMaxBytes = current.ScreenshotMaxBytes,
        ShowRequestHeaders = current.ShowRequestHeaders,
        ShowResponseHeaders = current.ShowResponseHeaders
    };

    private static int ReadInt(JsonElement obj, string name, int fallback, SettingLimit limit)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return fallback;
        if (!value.TryGetDouble(out var raw) || !double.IsFinite(raw))
            return fallback;
        return (int)Math.Min(limit.Max, Math.Max(limit.Min, Math.Round(raw)));
    }

    private static bool ReadBool(JsonElement obj, string name, bool fallback)
    {
        if (!obj.TryGetProperty(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }
}

public readonly record struct SettingLimit(int Min, int Max, int Default);

public static class Limits
{
    public static readonly SettingLimit LogLimit = new(1, 5_000, 500);
    public static readonly SettingLimit QueryLimit = new(1_000, 500_000, 30_000);
    public static readonly SettingLimit StringSizeLimit = new(100, 100_000, 500);
    public static readonly SettingLimit MaxLogSize = new(1_000, 1_000_000, 20_000);
    public static readonly SettingLimit ScreenshotMaxBytes = new(50_000, 9_000_000, 3_000_000);
}
