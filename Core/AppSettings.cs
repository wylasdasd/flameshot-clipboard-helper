using System.Text.Json;

namespace FlameshotClipboardHelper;

internal sealed class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlameshotClipboardHelper");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string WatchFolder { get; set; } = DefaultWatchFolder();

    public bool StartAtLogin { get; set; }

    /// <summary>auto, zh-CN, or en</summary>
    public string Language { get; set; } = "auto";

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(WatchFolder);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static string DefaultWatchFolder()
    {
        var fromFlameshot = TryReadFlameshotSavePath();
        if (!string.IsNullOrWhiteSpace(fromFlameshot))
            return fromFlameshot;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Flameshot");
    }

    private static string? TryReadFlameshotSavePath()
    {
        var iniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "flameshot",
            "flameshot.ini");

        if (!File.Exists(iniPath))
            return null;

        foreach (var line in File.ReadLines(iniPath))
        {
            if (!line.StartsWith("savePath=", StringComparison.Ordinal))
                continue;

            var path = line["savePath=".Length..].Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        return null;
    }
}
