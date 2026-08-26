<div align="right">

[English](./README.md) | **简体中文**

</div>

# Flameshot 剪贴板助手

Windows 托盘小工具，解决 Flameshot 复制截图时剪贴板文件名重复（如 `image.png`）的问题，让你能连续粘贴多张截图。

## 功能

- 监视 Flameshot 截图保存目录，检测到新 PNG 后自动更新剪贴板
- 剪贴板同时包含 **图片预览** 和 **文件引用**（真实 PNG 路径与文件名）
- 粘贴到 Cursor、浏览器、聊天窗口时能看到图片，且每次文件名不同
- 系统托盘运行，不监听键盘、不拦截 Flameshot 的 Ctrl+C
- 可配置监视文件夹、开机自启、界面语言（中文 / English / 自动）
- 首次运行会尝试从 `%APPDATA%\flameshot\flameshot.ini` 读取 `savePath`

## 工作原理

```
Flameshot 截图 → Ctrl+C → 保存 xxx.png 到磁盘
        ↓
本程序检测到新文件
        ↓
剪贴板：① 图片预览  ② 文件引用（唯一路径）
        ↓
目标应用 Ctrl+V 粘贴
```

## Flameshot 必设项

1. **Save image after copy**（复制后保存）—— 必须开启
2. **Use fixed path for screenshots to save**（固定保存路径）
3. 建议关闭 **Use JPG format for clipboard**（使用 PNG）
4. Flameshot 保存路径必须与程序的 **监视文件夹** 完全一致（不是上一级目录）

## 构建与运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 和 Windows。

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\FlameshotClipboardHelper.exe
```

## 设置

托盘右键 → **设置…**

| 选项 | 说明 |
|------|------|
| 监视文件夹 | Flameshot 实际写入 `.png` 的目录 |
| 开机自启 | 登录后自动启动 |
| 语言 | 自动（跟随系统）/ 中文 / English |

配置文件：`%LOCALAPPDATA%\FlameshotClipboardHelper\settings.json`

## 日常使用

1. 保持托盘图标在运行
2. Flameshot 截图 → **Ctrl+C**
3. 等待托盘提示「已更新剪贴板：xxx.png」（约 0.5 秒）
4. 在目标窗口 **Ctrl+V** 粘贴

## 常见问题

- **Win+V 只有路径、没有图片？**  
  监视目录与 Flameshot 保存目录不一致，或 PNG 尚未写完。
- **没有「已更新剪贴板」提示？**  
  检查 Flameshot 是否开启 Save image after copy。
- **仍提示 image.png 重名？**  
  粘贴前等待本程序更新剪贴板。

## 项目结构

```
Core/          设置、剪贴板逻辑、文件夹监视
Forms/         使用说明与设置窗口
Tray/          系统托盘
Program.cs     入口
```

## 许可

个人使用可自由修改。
