<div align="right">

**English** | [简体中文](./README.zh-CN.md)

</div>

# Flameshot Clipboard Helper

**Windows only.** A system-tray app that watches a screenshot **save folder** and refreshes the clipboard when a new PNG appears.

It does **not** hook the keyboard, capture screenshots, or talk to Flameshot directly. It only monitors the directory where screenshots are saved. [Flameshot](https://flameshot.org/) is used below as an example.

## The problem (Flameshot example)

With Flameshot, **Ctrl+C** after a capture often puts the same filename (`image.png`) on the clipboard. Pasting into Cursor, a browser, or chat repeatedly fails or overwrites the previous image.

## What this app does

1. You configure a **watch folder** — the directory where PNG files land after copy/save.
2. When a new `.png` file appears, the app writes to the clipboard:
   - an **image preview**, and
   - a **file reference** with the real path and unique filename.
3. **Ctrl+V** in the target app pastes the image with a distinct name each time.

```
Flameshot: capture → Ctrl+C → saves 2025-08-27_12-30-45.png
        ↓
This app (folder watch only) sees the new file
        ↓
Clipboard updated: image + file path
        ↓
Ctrl+V anywhere
```

## Flameshot setup (example)

Install Flameshot, then in its settings:

| Flameshot setting | Value |
|-------------------|--------|
| Save image after copy | **ON** (required — app needs a saved PNG) |
| Use fixed path for screenshots to save | **ON** |
| Save path | e.g. `C:\Users\you\Pictures\Flameshot` |
| Use JPG format for clipboard | **OFF** (PNG recommended) |

In this app (**Settings → Watch folder**), set the **same path** Flameshot uses — the folder where `.png` files are written, not a parent folder like `Pictures`.

On first run, the app tries to read `savePath` from `%APPDATA%\flameshot\flameshot.ini`.

## Requirements

- **Windows 10/11** (WinForms tray app; no macOS or Linux)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- A screenshot tool that saves PNGs to a fixed folder on copy (Flameshot shown above)

## Build & run

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\FlameshotClipboardHelper.exe
```

## Settings

Tray right-click → **Settings…**

| Option | Description |
|--------|-------------|
| Watch folder | Screenshot save directory to monitor (`*.png`) |
| Start at login | Launch after sign-in |
| Language | Auto / 中文 / English |

Config: `%LOCALAPPDATA%\FlameshotClipboardHelper\settings.json`

## Daily use (Flameshot)

1. Keep the tray icon running.
2. Flameshot: select region → **Ctrl+C**.
3. Wait for tray message `Clipboard updated: xxx.png` (~0.5s).
4. **Ctrl+V** in Cursor, browser, or chat.

## FAQ

- **Win+V shows only a folder path, no image thumbnail?**  
  Flameshot saves to `C:\Users\you\Pictures\Flameshot`, but the watch folder is set to `C:\Users\you\Pictures` — they must match exactly. After Flameshot **Ctrl+C**, confirm a new `.png` appears in that folder before pasting.

- **No tray message after Flameshot Ctrl+C?**  
  In Flameshot → Settings, turn on **Save image after copy**. Without it, Flameshot copies to the clipboard but does not write a file, so this app has nothing to watch.

- **Flameshot saves files, but this app still does nothing?**  
  Check **Use fixed path for screenshots to save** is ON and compare paths:
  - Flameshot: Settings → save path (or `savePath` in `%APPDATA%\flameshot\flameshot.ini`)
  - This app: Settings → Watch folder  
  Both must be the same folder.

- **Cursor / browser still says `image.png` already exists?**  
  Flameshot **Ctrl+C** → wait for tray `Clipboard updated: 2025-08-27_12-30-45.png` → then **Ctrl+V**. Pasting immediately uses Flameshot's old clipboard content (`image.png`).

- **Screenshot is huge and Win+V has no preview?**  
  Files over ~15 MB may get a file reference only (no image preview). The PNG is still under Flameshot's save folder; paste or attach the file directly.

## Project layout

```
Core/          Settings, clipboard logic, folder watcher
Forms/         Help and settings dialogs
Tray/          System tray
Program.cs     Entry point
```

## License

Use and modify as you like for personal use.
