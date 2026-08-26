using System.Diagnostics;
using FlameshotClipboardHelper.Forms;

namespace FlameshotClipboardHelper.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Form _loopForm;
    private readonly NotifyIcon _tray;
    private readonly AppSettings _settings;
    private readonly ScreenshotWatcher _watcher;

    public TrayApplicationContext()
    {
        _loopForm = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            Opacity = 0,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            Icon = AppIcon.Tray,
        };
        MainForm = _loopForm;

        _settings = AppSettings.Load();
        Locale.Apply(_settings);
        Directory.CreateDirectory(_settings.WatchFolder);
        StartupHelper.Apply(_settings.StartAtLogin);

        _watcher = new ScreenshotWatcher(_loopForm, OnClipboardPushed);

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Tray,
            Visible = true,
            Text = L.AppTitle,
        };

        _tray.ContextMenuStrip = BuildMenu();
        _tray.DoubleClick += (_, _) => OpenHelp();
        UpdateTooltip();

        _loopForm.Load += (_, _) => _watcher.Start(_settings.WatchFolder);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(L.MenuHelp, null, (_, _) => OpenHelp());
        menu.Items.Add(L.MenuSettings, null, (_, _) => OpenSettings());
        menu.Items.Add(L.MenuOpenFolder, null, (_, _) => OpenWatchFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.MenuExit, null, (_, _) => Exit());

        return menu;
    }

    private static void OpenHelp()
    {
        using var form = new HelpForm();
        form.ShowDialog();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(new AppSettings
        {
            WatchFolder = _settings.WatchFolder,
            StartAtLogin = _settings.StartAtLogin,
            Language = _settings.Language,
        });

        if (form.ShowDialog() != DialogResult.OK)
            return;

        var languageChanged = _settings.Language != form.Settings.Language;

        _settings.WatchFolder = form.Settings.WatchFolder;
        _settings.StartAtLogin = form.Settings.StartAtLogin;
        _settings.Language = form.Settings.Language;
        _settings.Save();

        StartupHelper.Apply(_settings.StartAtLogin);
        _watcher.Start(_settings.WatchFolder);

        if (languageChanged)
        {
            Locale.Apply(_settings);
            _tray.ContextMenuStrip?.Dispose();
            _tray.ContextMenuStrip = BuildMenu();
        }

        UpdateTooltip();

        _tray.ShowBalloonTip(
            2000,
            L.AppTitle,
            L.SettingsSavedWatching(_settings.WatchFolder),
            ToolTipIcon.Info);
    }

    private void OpenWatchFolder()
    {
        Directory.CreateDirectory(_settings.WatchFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.WatchFolder,
            UseShellExecute = true,
        });
    }

    private void OnClipboardPushed(string path, bool ok)
    {
        var name = Path.GetFileName(path);
        _tray.Text = ok
            ? L.ClipboardUpdated(name)
            : L.ClipboardUpdateFailed(name);

        if (!ok)
        {
            _tray.ShowBalloonTip(
                2500,
                L.AppTitle,
                L.ClipboardWriteFailed(name),
                ToolTipIcon.Warning);
        }
    }

    private void UpdateTooltip()
    {
        var folder = _settings.WatchFolder;
        _tray.Text = folder.Length <= 60
            ? L.WatchingTooltip(folder)
            : L.WatchingTooltipTruncated(folder[^57..]);
    }

    private void Exit()
    {
        _watcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _loopForm.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _tray.Dispose();
            _loopForm.Dispose();
        }

        base.Dispose(disposing);
    }
}
