using System.Diagnostics;

namespace FlameshotClipboardHelper.Core;

internal sealed record TrayState(bool Visible, string ToolTipText);

internal sealed record ClipboardPushResult(string FileName, bool Success);

internal sealed record SettingsApplyResult(
    bool Saved,
    bool LanguageChanged,
    bool TrayVisibilityChanged,
    string? InfoMessage);

internal sealed class AppController : IDisposable
{
    private readonly IClipboardWriter _clipboard;
    private readonly ScreenshotWatcher _watcher;

    public AppSettings Settings { get; private set; }

    public event Action<TrayState>? TrayStateChanged;
    public event Action<ClipboardPushResult>? ClipboardPushed;

    public AppController(IClipboardWriter clipboard, Action<Action> marshalToUi)
    {
        _clipboard = clipboard;
        Settings = AppSettings.Load();
        Locale.Apply(Settings);
        _watcher = new ScreenshotWatcher(marshalToUi, path =>
        {
            var ok = ScreenshotClipboardService.TryPush(path, _clipboard);
            ClipboardPushed?.Invoke(new ClipboardPushResult(Path.GetFileName(path), ok));
        });
    }

    public void Start()
    {
        Directory.CreateDirectory(Settings.WatchFolder);
        StartupHelper.Apply(Settings.StartAtLogin);
        _watcher.Start(Settings.WatchFolder);
        PublishTrayState();
    }

    public AppSettings CreateSettingsSnapshot() => new()
    {
        WatchFolder = Settings.WatchFolder,
        StartAtLogin = Settings.StartAtLogin,
        HideTrayIcon = Settings.HideTrayIcon,
        Language = Settings.Language,
    };

    public SettingsApplyResult ApplySettings(AppSettings updated)
    {
        var languageChanged = Settings.Language != updated.Language;
        var trayVisibilityChanged = Settings.HideTrayIcon != updated.HideTrayIcon;

        Settings.WatchFolder = updated.WatchFolder;
        Settings.StartAtLogin = updated.StartAtLogin;
        Settings.HideTrayIcon = updated.HideTrayIcon;
        Settings.Language = updated.Language;
        Settings.Save();

        if (languageChanged)
            Locale.Apply(Settings);

        StartupHelper.Apply(Settings.StartAtLogin);
        _watcher.Start(Settings.WatchFolder);
        PublishTrayState();

        string? infoMessage = null;
        if (!Settings.HideTrayIcon)
            infoMessage = L.SettingsSavedWatching(Settings.WatchFolder);
        else if (trayVisibilityChanged)
            infoMessage = L.HideTrayHint;

        return new SettingsApplyResult(true, languageChanged, trayVisibilityChanged, infoMessage);
    }

    public void OpenWatchFolder()
    {
        Directory.CreateDirectory(Settings.WatchFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = Settings.WatchFolder,
            UseShellExecute = true,
        });
    }

    public void OnClipboardPushResult(ClipboardPushResult result)
    {
        if (Settings.HideTrayIcon)
            return;

        PublishTrayState(result.Success
            ? L.ClipboardUpdated(result.FileName)
            : L.ClipboardUpdateFailed(result.FileName));
    }

    public void PublishTrayState(string? overrideToolTip = null)
    {
        if (Settings.HideTrayIcon)
        {
            TrayStateChanged?.Invoke(new TrayState(false, string.Empty));
            return;
        }

        var toolTip = overrideToolTip ?? BuildWatchingToolTip();
        TrayStateChanged?.Invoke(new TrayState(true, toolTip));
    }

    private string BuildWatchingToolTip()
    {
        var folder = Settings.WatchFolder;
        return folder.Length <= 60
            ? L.WatchingTooltip(folder)
            : L.WatchingTooltipTruncated(folder[^57..]);
    }

    public void Dispose() => _watcher.Dispose();
}
