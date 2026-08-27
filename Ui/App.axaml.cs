using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FlameshotClipboardHelper.Core;
using FlameshotClipboardHelper.Ui.Services;
using FlameshotClipboardHelper.Ui.Views;

namespace FlameshotClipboardHelper.Ui;

internal partial class App : Avalonia.Application
{
    private AppController? _controller;
    private HiddenHostWindow? _hostWindow;
    private TrayIcon? _tray;
    private NativeMenu? _trayMenu;
    private EventWaitHandle? _showSettingsWaitHandle;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _hostWindow = new HiddenHostWindow();
            desktop.MainWindow = _hostWindow;

            var clipboard = new WindowsClipboardWriter();
            _controller = new AppController(clipboard, action => Dispatcher.UIThread.Post(action));
            _controller.TrayStateChanged += OnTrayStateChanged;
            _controller.ClipboardPushed += OnClipboardPushed;

            _tray = new TrayIcon
            {
                Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FlameshotClipboardHelper/app.ico"))),
                ToolTipText = L.AppTitle,
            };
            _tray.Clicked += (_, _) => OpenHelp();
            RebuildTrayMenu();

            var trayIcons = new TrayIcons();
            trayIcons.Add(_tray);
            TrayIcon.SetIcons(this, trayIcons);

            _showSettingsWaitHandle = SingleInstance.ShowSettingsEvent;
            ThreadPool.RegisterWaitForSingleObject(
                _showSettingsWaitHandle,
                (_, _) => Dispatcher.UIThread.Post(OpenSettings),
                null,
                Timeout.Infinite,
                false);

            _controller.Start();
            _hostWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayStateChanged(TrayState state)
    {
        if (_tray is null)
            return;

        _tray.IsVisible = state.Visible;
        _tray.ToolTipText = state.ToolTipText;
    }

    private void OnClipboardPushed(ClipboardPushResult result)
    {
        _controller?.OnClipboardPushResult(result);

        if (!result.Success && _controller?.Settings.HideTrayIcon == false)
            NativeMessageBox.ShowInfo(L.ClipboardWriteFailed(result.FileName), L.AppTitle);
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null)
            return;

        _trayMenu = new NativeMenu
        {
            Items =
            {
                CreateMenuItem(L.MenuHelp, OpenHelp),
                CreateMenuItem(L.MenuSettings, OpenSettings),
                CreateMenuItem(L.MenuOpenFolder, OpenWatchFolder),
                new NativeMenuItemSeparator(),
                CreateMenuItem(L.MenuExit, ExitApp),
            },
        };
        _tray.Menu = _trayMenu;
    }

    private static NativeMenuItem CreateMenuItem(string header, Action action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenHelp()
    {
        var help = new HelpWindow();
        if (_hostWindow is not null)
            help.ShowDialog(_hostWindow);
        else
            help.Show();
    }

    private async void OpenSettings()
    {
        if (_controller is null || _hostWindow is null)
            return;

        var dialog = new SettingsWindow(_controller.CreateSettingsSnapshot());
        if (await dialog.ShowDialog<bool?>(_hostWindow) != true || dialog.Result is null)
            return;

        var result = _controller.ApplySettings(dialog.Result);
        if (result.LanguageChanged)
            RebuildTrayMenu();

        if (result.InfoMessage is not null)
            NativeMessageBox.ShowInfo(result.InfoMessage, L.AppTitle);
    }

    private void OpenWatchFolder() => _controller?.OpenWatchFolder();

    private void ExitApp()
    {
        _controller?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
