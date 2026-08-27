using System.Globalization;

namespace FlameshotClipboardHelper.Core;

internal enum AppLanguage
{
    Auto,
    ZhCn,
    En,
}

internal static class Locale
{
    public static AppLanguage Setting { get; private set; } = AppLanguage.Auto;

    public static bool IsChinese => Resolve() == AppLanguage.ZhCn;

    public static void Apply(AppSettings settings)
    {
        Setting = settings.Language switch
        {
            "zh-CN" => AppLanguage.ZhCn,
            "en" => AppLanguage.En,
            _ => AppLanguage.Auto,
        };
    }

    public static string ToCode(AppLanguage language) => language switch
    {
        AppLanguage.ZhCn => "zh-CN",
        AppLanguage.En => "en",
        _ => "auto",
    };

    public static AppLanguage FromCode(string? code) => code switch
    {
        "zh-CN" => AppLanguage.ZhCn,
        "en" => AppLanguage.En,
        _ => AppLanguage.Auto,
    };

    private static AppLanguage Resolve()
    {
        if (Setting != AppLanguage.Auto)
            return Setting;

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
            ? AppLanguage.ZhCn
            : AppLanguage.En;
    }
}

internal static class L
{
    public static string AppTitle => T("Flameshot 剪贴板助手", "Flameshot Clipboard Helper");
    public static string MenuHelp => T("使用说明", "Help");
    public static string MenuSettings => T("设置…", "Settings…");
    public static string MenuOpenFolder => T("打开监视文件夹", "Open watch folder");
    public static string MenuExit => T("退出", "Exit");
    public static string HelpTitle => T("使用说明", "Help");
    public static string Close => T("关闭", "Close");
    public static string Browse => T("浏览…", "Browse…");
    public static string StartAtLogin => T("开机自启", "Start with Windows");
    public static string HideTrayIcon => T("不显示托盘图标", "Hide tray icon");
    public static string HideTrayHint => T(
        "隐藏后程序仍在后台监视；再次运行 exe 可打开设置",
        "App keeps watching in background; run the exe again to open Settings");
    public static string AlreadyRunning => T(
        "程序已在运行，无法打开设置。",
        "Already running, but could not open Settings.");
    public static string Save => T("保存", "Save");
    public static string Cancel => T("取消", "Cancel");
    public static string LanguageLabel => T("语言：", "Language:");
    public static string LanguageAuto => T("自动（跟随系统）", "Auto (system)");
    public static string LanguageZh => "中文";
    public static string LanguageEn => "English";
    public static string WatchFolderRequired => T("请填写监视文件夹。", "Please enter a watch folder.");
    public static string WatchFolderLabel => T(
        "监视文件夹（Flameshot 保存路径）：",
        "Watch folder (Flameshot save path):");
    public static string SettingsHint => T(
        "新 PNG 保存后写入剪贴板（图片 + 文件引用）",
        "When a new PNG is saved, update the clipboard (image + file reference)");
    public static string FolderBrowseDescription => T(
        "选择 Flameshot 截图保存文件夹",
        "Select Flameshot screenshot save folder");

    public static string SettingsSavedWatching(string folder) => T(
        $"已开始监视：{folder}",
        $"Now watching: {folder}");

    public static string ClipboardUpdated(string name) => T(
        $"已更新剪贴板：{name}",
        $"Clipboard updated: {name}");

    public static string ClipboardUpdateFailed(string name) => T(
        $"剪贴板更新失败：{name}",
        $"Clipboard update failed: {name}");

    public static string ClipboardWriteFailed(string name) => T(
        $"无法写入剪贴板：{name}",
        $"Could not write to clipboard: {name}");

    public static string WatchingTooltip(string folder) => T(
        $"监视：{folder}",
        $"Watching: {folder}");

    public static string WatchingTooltipTruncated(string suffix) => T(
        $"监视：…{suffix}",
        $"Watching: …{suffix}");

    private static string T(string zh, string en) => Locale.IsChinese ? zh : en;
}
