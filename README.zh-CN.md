<div align="right">

[English](./README.md) | **简体中文**

</div>

# Flameshot 剪贴板助手

**仅支持 Windows。** 系统托盘程序，只监视截图**保存目录**，有新 PNG 写入时更新剪贴板。

**不**监听键盘、不截屏、不与 Flameshot 通信，只做文件夹监视。下文以 [Flameshot](https://flameshot.org/) 为例说明用法。

## 要解决的问题（以 Flameshot 为例）

Flameshot 截图后按 **Ctrl+C**，剪贴板里的文件名经常是同一个（`image.png`）。在 Cursor、浏览器、聊天里连续粘贴会失败或覆盖上一张。

## 本程序做什么

1. 配置一个 **监视文件夹** —— 截图复制/保存后 PNG 实际写入的目录。
2. 检测到新 `.png` 后，向剪贴板写入：
   - **图片预览**
   - **文件引用**（真实路径与唯一文件名）
3. 在目标处 **Ctrl+V** 即可粘贴，每次文件名不同。

```
Flameshot：截图 → Ctrl+S → 保存 2025-08-27_12-30-45.png
        ↓
本程序（仅监视目录）发现新文件
        ↓
更新剪贴板：图片 + 文件路径
        ↓
任意处 Ctrl+V
```

## Flameshot 配置（示例）

安装 Flameshot 后，在其设置中：

| Flameshot 选项 | 建议 |
|----------------|------|
| Save image after copy（复制后保存） | **开启**（必须，否则没有 PNG 可监视） |
| Use fixed path for screenshots to save（固定保存路径） | **开启** |
| 保存路径 | 如 `C:\Users\你\Pictures\Flameshot` |
| Use JPG format for clipboard | **关闭**（建议 PNG） |

在本程序 **设置 → 监视文件夹** 中填写 **与 Flameshot 相同的路径** —— 必须是实际写入 `.png` 的文件夹，不是 `Pictures` 等上一级目录。

首次运行会尝试从 `%APPDATA%\flameshot\flameshot.ini` 读取 `savePath`。

## 运行环境

- **Windows 10/11**（Avalonia 托盘程序，不支持 macOS / Linux）
- 构建需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 需要一款在复制时将 PNG 保存到固定目录的截图工具（下文以 Flameshot 为例）

## 构建与运行

```powershell
dotnet build -c Release
.\bin\Release\net10.0-windows\FlameshotClipboardHelper.exe
```

### 发布（Native AOT，单文件，无需安装 .NET）

```powershell
dotnet publish -c Release -r win-x64 `
  -p:PublishAot=true `
  -p:OptimizationPreference=Size `
  -o publish/win-x64-aot
```

输出：`publish/win-x64-aot\FlameshotClipboardHelper.exe`（约 15–25 MB）

### 发布（自包含单文件，非 AOT）

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish/win-x64
```

输出：`publish/win-x64\FlameshotClipboardHelper.exe`

## 设置

托盘右键 → **设置…**

| 选项 | 说明 |
|------|------|
| 监视文件夹 | 要监视的截图保存目录（`*.png`） |
| 开机自启 | 系统启动 Windows 后自动运行 |
| 不显示托盘图标 | 后台运行无托盘；再次运行 exe 打开设置 |
| 语言 | 自动 / 中文 / English |

配置：`%LOCALAPPDATA%\FlameshotClipboardHelper\settings.json`

## 日常使用（Flameshot）

1. 保持托盘图标在运行。
2. Flameshot 框选区域 → **Ctrl+C**。
3. 等待托盘提示「已更新剪贴板：xxx.png」（约 0.5 秒）。
4. 在 Cursor、浏览器或聊天里 **Ctrl+V**。

## 常见问题

- **Win+V 只有文件夹路径，没有图片缩略图？**  
  Flameshot 保存到 `C:\Users\你\Pictures\Flameshot`，但监视文件夹填的是 `C:\Users\你\Pictures` —— 必须完全一致。Flameshot **Ctrl+C** 后，先确认该目录里出现了新的 `.png` 再粘贴。

- **Flameshot Ctrl+C 后，托盘没有「已更新剪贴板」？**  
  打开 Flameshot → 设置，开启 **Save image after copy（复制后保存）**。未开启时 Flameshot 只写剪贴板、不落盘，本程序监视不到新文件。

- **Flameshot 目录里已有新 PNG，程序仍无反应？**  
  确认 **Use fixed path for screenshots to save（固定保存路径）** 已开启，并核对两处路径：
  - Flameshot：设置里的保存路径（或 `%APPDATA%\flameshot\flameshot.ini` 中的 `savePath`）
  - 本程序：设置 → 监视文件夹  
  两处必须是同一个文件夹。

- **Cursor / 浏览器仍提示 `image.png` 已存在？**  
  Flameshot **Ctrl+C** → 等待托盘显示「已更新剪贴板：2025-08-27_12-30-45.png」→ 再 **Ctrl+V**。贴太快时用的还是 Flameshot 原来的剪贴板（`image.png`）。

- **截图很大，Win+V 没有预览？**  
  超过约 15 MB 的文件可能只写入文件引用、不写图片预览。PNG 仍在 Flameshot 保存目录中，可直接粘贴或附加该文件。

- **隐藏托盘后，怎么打开设置或恢复托盘？**  
  程序只允许运行一个实例。托盘隐藏后 **再运行一次 `FlameshotClipboardHelper.exe`** —— 已在运行的实例会弹出设置（不会启动第二个进程）。取消勾选 **不显示托盘图标** → **保存** 即可。

  若无效，编辑 `%LOCALAPPDATA%\FlameshotClipboardHelper\settings.json`，将 `"HideTrayIcon"` 改为 `false` 后重启：

  ```powershell
  taskkill /IM FlameshotClipboardHelper.exe /F
  .\FlameshotClipboardHelper.exe
  ```

- **托盘可见时重复运行 exe？**  
  第二次运行会让已有实例打开设置窗口，然后立即退出。

## 项目结构

```
Core/          业务逻辑（设置、监视、剪贴板编排），不依赖 UI
Ui/            Avalonia 界面（托盘、设置、帮助）与平台剪贴板实现
Program.cs     入口
```

## 许可

个人使用可自由修改。
