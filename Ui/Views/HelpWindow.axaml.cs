using Avalonia.Controls;
using Avalonia.Interactivity;
using FlameshotClipboardHelper.Core;

namespace FlameshotClipboardHelper.Ui.Views;

internal sealed partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Title = L.HelpTitle;
        HelpTextBox.Text = Locale.IsChinese ? HelpText.Zh : HelpText.En;
        CloseButton.Content = L.Close;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
