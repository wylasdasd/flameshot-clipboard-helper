using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FlameshotClipboardHelper.Core;

namespace FlameshotClipboardHelper.Ui.Views;

internal sealed partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LanguageItem[] _languageItems;

    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Title = $"{L.AppTitle}  v{AppInfo.Version}";
        FolderLabel.Text = L.WatchFolderLabel;
        FolderBox.Text = settings.WatchFolder;
        BrowseButton.Content = L.Browse;
        StartupBox.Content = L.StartAtLogin;
        StartupBox.IsChecked = settings.StartAtLogin;
        HideTrayBox.Content = L.HideTrayIcon;
        HideTrayBox.IsChecked = settings.HideTrayIcon;
        LanguageLabel.Text = L.LanguageLabel;
        HintLabel.Text = L.SettingsHint;
        HelpButton.Content = L.MenuHelp;
        SaveButton.Content = L.Save;
        CancelButton.Content = L.Cancel;

        _languageItems =
        [
            new(L.LanguageAuto, AppLanguage.Auto),
            new(L.LanguageZh, AppLanguage.ZhCn),
            new(L.LanguageEn, AppLanguage.En),
        ];
        LanguageBox.ItemsSource = _languageItems;
        LanguageBox.SelectedIndex = Array.FindIndex(
            _languageItems,
            item => item.Language == Locale.FromCode(settings.Language));
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.FolderBrowseDescription,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                ToFolderUri(Directory.Exists(FolderBox.Text)
                    ? FolderBox.Text
                    : AppSettings.DefaultWatchFolder())),
        });

        if (folders.Count > 0)
            FolderBox.Text = folders[0].Path.LocalPath;
    }

    private static Uri ToFolderUri(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
        return new Uri("file:///" + fullPath.Replace('\\', '/'));
    }

    private void OnHelpClick(object? sender, RoutedEventArgs e)
    {
        var help = new HelpWindow();
        help.ShowDialog(this);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var folder = (FolderBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            await ShowError(L.WatchFolderRequired);
            return;
        }

        var selected = LanguageBox.SelectedItem as LanguageItem
            ?? _languageItems[0];

        Result = new AppSettings
        {
            WatchFolder = folder,
            StartAtLogin = StartupBox.IsChecked == true,
            HideTrayIcon = HideTrayBox.IsChecked == true,
            Language = Locale.ToCode(selected.Language),
        };
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async Task ShowError(string message)
    {
        var dialog = new Window
        {
            Title = Title,
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var ok = new Button { Content = L.Close, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.Close();
        panel.Children.Add(ok);
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private sealed record LanguageItem(string Label, AppLanguage Language)
    {
        public override string ToString() => Label;
    }
}
