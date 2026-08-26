namespace FlameshotClipboardHelper.Forms;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _folderBox;
    private readonly CheckBox _startupBox;
    private readonly CheckBox _hideTrayBox;
    private readonly ComboBox _languageBox;
    private readonly AppSettings _settings;

    public AppSettings Settings => _settings;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = $"{L.AppTitle}  v{AppInfo.Version}";
        Icon = AppIcon.Tray;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 252);

        var folderLabel = new Label
        {
            Text = L.WatchFolderLabel,
            AutoSize = true,
            Location = new Point(12, 16),
        };

        _folderBox = new TextBox
        {
            Text = settings.WatchFolder,
            Location = new Point(12, 40),
            Width = 360,
        };

        var browse = new Button
        {
            Text = L.Browse,
            Location = new Point(378, 38),
            Width = 70,
        };
        browse.Click += (_, _) => BrowseFolder();

        _startupBox = new CheckBox
        {
            Text = L.StartAtLogin,
            AutoSize = true,
            Location = new Point(12, 78),
            Checked = settings.StartAtLogin,
        };

        _hideTrayBox = new CheckBox
        {
            Text = L.HideTrayIcon,
            AutoSize = true,
            Location = new Point(12, 102),
            Checked = settings.HideTrayIcon,
        };

        var languageLabel = new Label
        {
            Text = L.LanguageLabel,
            AutoSize = false,
            Size = new Size(76, 23),
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(12, 126),
        };

        _languageBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(92, 124),
            Width = 200,
            IntegralHeight = false,
            DropDownWidth = 220,
        };
        var languageItems = new LanguageItem[]
        {
            new(L.LanguageAuto, AppLanguage.Auto),
            new(L.LanguageZh, AppLanguage.ZhCn),
            new(L.LanguageEn, AppLanguage.En),
        };
        _languageBox.Items.AddRange(languageItems);
        _languageBox.SelectedIndex = Array.FindIndex(
            languageItems,
            item => item.Language == Locale.FromCode(settings.Language));

        var hint = new Label
        {
            Text = L.SettingsHint,
            AutoSize = false,
            Size = new Size(436, 32),
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 154),
        };

        var version = new Label
        {
            Text = $"v{AppInfo.Version}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(100, 206),
        };

        var help = new Button
        {
            Text = L.MenuHelp,
            Location = new Point(12, 212),
            Width = 75,
        };
        help.Click += (_, _) =>
        {
            using var form = new HelpForm();
            form.ShowDialog(this);
        };

        var save = new Button
        {
            Text = L.Save,
            DialogResult = DialogResult.OK,
            Location = new Point(292, 212),
            Width = 75,
        };

        var cancel = new Button
        {
            Text = L.Cancel,
            DialogResult = DialogResult.Cancel,
            Location = new Point(373, 212),
            Width = 75,
        };

        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange([
            folderLabel, _folderBox, browse, _startupBox, _hideTrayBox,
            languageLabel, _languageBox, hint, version, help, save, cancel,
        ]);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            base.OnFormClosing(e);
            return;
        }

        var folder = _folderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(L.WatchFolderRequired, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        _settings.WatchFolder = folder;
        _settings.StartAtLogin = _startupBox.Checked;
        _settings.HideTrayIcon = _hideTrayBox.Checked;
        _settings.Language = Locale.ToCode(((LanguageItem)_languageBox.SelectedItem!).Language);
        base.OnFormClosing(e);
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.FolderBrowseDescription,
            SelectedPath = Directory.Exists(_folderBox.Text) ? _folderBox.Text : AppSettings.DefaultWatchFolder(),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _folderBox.Text = dialog.SelectedPath;
    }

    private sealed record LanguageItem(string Label, AppLanguage Language)
    {
        public override string ToString() => Label;
    }
}
