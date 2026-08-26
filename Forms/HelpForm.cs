namespace FlameshotClipboardHelper.Forms;

internal sealed class HelpForm : Form
{
    public HelpForm()
    {
        Text = L.HelpTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 480);
        Icon = AppIcon.Tray;

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = new Font(Locale.IsChinese ? "Microsoft YaHei UI" : "Segoe UI", 9.5f),
            Location = new Point(12, 12),
            Size = new Size(516, 420),
            Text = Locale.IsChinese ? HelpText.Zh : HelpText.En,
        };

        var close = new Button
        {
            Text = L.Close,
            DialogResult = DialogResult.OK,
            Location = new Point(453, 442),
            Width = 75,
        };

        AcceptButton = close;
        Controls.Add(text);
        Controls.Add(close);
    }
}

internal static class HelpText
{
    public const string Zh =
"""
【作用】
Flameshot 按 Ctrl+C 复制时，粘贴到 Cursor / 浏览器 / 聊天窗口
容易出现同名文件（如 image.png），无法连续贴多张图。

本程序配合 Flameshot 的「复制后保存」使用：
监视保存目录，当有新 PNG 出现时，把「图片 + 文件引用」
一起写入剪贴板——粘贴时能看到图片，且文件名来自真实 PNG 路径。

【工作原理】
Flameshot Ctrl+C → 保存 xxx.png 到磁盘
        ↓
本程序检测到新文件
        ↓
剪贴板写入：① 图片预览  ② 文件引用（带唯一文件名）
        ↓
目标处 Ctrl+V 粘贴

【Flameshot 必设项】
1. Save image after copy（复制后保存）—— 必须开启
2. Use fixed path for screenshots to save（固定保存路径）
3. 关闭 Use JPG format for clipboard（建议 PNG）
4. 保存路径 = 本程序「监视文件夹」（必须完全一致）

注意：监视路径必须是 Flameshot 实际写入 .png 的文件夹，
不是上一级目录。例如 Flameshot 存到
  C:\Users\你\Pictures\Flameshot
则监视文件夹也要填这个路径，而不是 Pictures 或 Screenshots。

【本程序设置】
1. 托盘右键 → 设置 → 确认监视文件夹
2. 首次运行会尝试读取 flameshot.ini 里的 savePath
3. 可勾选「开机自启」「不显示托盘图标」
4. 隐藏托盘后程序仍在后台监视；再次运行 exe 可打开设置
5. 配置保存在：
   %LOCALAPPDATA%\FlameshotClipboardHelper\settings.json

【日常使用】
1. 确保托盘图标在运行
2. Flameshot 截图 → Ctrl+C
3. 等待托盘提示「已更新剪贴板：2025-08-27_xxx.png」
4. Win+V 应能看到图片缩略图（不是只有文件夹路径）
5. 到聊天窗口 Ctrl+V 粘贴

【常见问题】
• Win+V 只有路径、没有图片？
  → 监视目录与 Flameshot 保存目录不一致，或 PNG 尚未保存完成
• 托盘没有「已更新剪贴板」提示？
  → 检查 Flameshot 是否开启 Save image after copy
• 仍提示 image.png 重名？
  → 确认粘贴前已等到本程序更新剪贴板（约 0.5 秒）

【其他说明】
• 不监听键盘，不拦截 Flameshot 的 Ctrl+C
• 超大截图（>15MB）可能只写入文件引用，不写图片预览
• 托盘双击或右键「使用说明」可再次打开本文
""";

    public const string En =
"""
[What it does]
When Flameshot copies with Ctrl+C, pasting into Cursor, browsers, or chat apps
often reuses the same filename (e.g. image.png), so you cannot paste many shots in a row.

This app works with Flameshot's "Save image after copy":
it watches the save folder and, when a new PNG appears, writes both an image preview
and a file reference to the clipboard — paste shows the image with a unique filename.

[How it works]
Flameshot Ctrl+C → saves xxx.png to disk
        ↓
This app detects the new file
        ↓
Clipboard: ① image preview  ② file reference (unique path)
        ↓
Ctrl+V in the target app

[Required Flameshot settings]
1. Save image after copy — must be ON
2. Use fixed path for screenshots to save
3. Turn OFF Use JPG format for clipboard (PNG recommended)
4. Save path must match this app's watch folder exactly

Note: the watch folder must be where Flameshot writes .png files,
not a parent folder. Example: if Flameshot saves to
  C:\Users\you\Pictures\Flameshot
set the watch folder to that path — not Pictures or Screenshots.

[App settings]
1. Tray right-click → Settings → confirm watch folder
2. On first run, savePath is read from flameshot.ini when possible
3. Optional: Start with Windows, Hide tray icon
4. When the tray is hidden, the app keeps watching; run the exe again to open Settings
5. Config file:
   %LOCALAPPDATA%\FlameshotClipboardHelper\settings.json

[Daily use]
1. Keep the tray icon running
2. Flameshot screenshot → Ctrl+C
3. Wait for tray message "Clipboard updated: 2025-08-27_xxx.png"
4. Win+V should show an image thumbnail (not just a folder path)
5. Ctrl+V in chat or editor

[FAQ]
• Win+V shows only a path, no image?
  → Watch folder does not match Flameshot's save folder, or PNG is not finished writing
• No "Clipboard updated" tray message?
  → Check Flameshot "Save image after copy"
• Still get image.png name conflicts?
  → Wait ~0.5s for this app to update the clipboard before pasting

[Other]
• Does not hook the keyboard or intercept Flameshot Ctrl+C
• Very large screenshots (>15MB) may get file reference only, no image preview
• Double-click tray or open Help from the menu to read this again
""";
}
