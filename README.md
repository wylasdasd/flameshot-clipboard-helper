<div align="right">

**English** | [简体中文](./README.zh-CN.md)

</div>

# Flameshot Clipboard Helper

Windows tray app that fixes Flameshot's duplicate `image.png` clipboard issue so you can paste many screenshots in a row.

## Features

- Watches Flameshot's screenshot save folder and updates the clipboard when a new PNG appears
- Writes both an **image preview** and a **file reference** (real PNG path and filename) to the clipboard
- Paste into Cursor, browsers, or chat apps with a visible image and a unique filename each time
- Runs in the system tray; does **not** hook the keyboard or intercept Flameshot's Ctrl+C
- Configurable watch folder, start at login, and UI language (中文 / English / Auto)
- On first run, tries to read `savePath` from `%APPDATA%\flameshot\flameshot.ini`

## How it works

```
Flameshot screenshot → Ctrl+C → saves xxx.png to disk
        ↓
This app detects the new file
        ↓
Clipboard: ① image preview  ② file reference (unique path)
        ↓
Ctrl+V in the target app
```

## Required Flameshot settings

1. **Save image after copy** — must be ON
2. **Use fixed path for screenshots to save**
3. Turn OFF **Use JPG format for clipboard** (PNG recommended)
4. Flameshot's save path must **exactly match** this app's watch folder (not a parent directory)

## Build & run

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows.

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\FlameshotClipboardHelper.exe
```

## Settings

Tray right-click → **Settings…**

| Option | Description |
|--------|-------------|
| Watch folder | Folder where Flameshot writes `.png` files |
| Start at login | Launch automatically after sign-in |
| Language | Auto (system) / 中文 / English |

Config file: `%LOCALAPPDATA%\FlameshotClipboardHelper\settings.json`

## Daily use

1. Keep the tray icon running
2. Flameshot screenshot → **Ctrl+C**
3. Wait for tray message "Clipboard updated: xxx.png" (~0.5s)
4. **Ctrl+V** in the target app

## FAQ

- **Win+V shows only a path, no image?**  
  Watch folder does not match Flameshot's save folder, or the PNG is still being written.
- **No "Clipboard updated" message?**  
  Check that Flameshot has Save image after copy enabled.
- **Still getting image.png conflicts?**  
  Wait for this app to update the clipboard before pasting.

## Project layout

```
Core/          Settings, clipboard logic, folder watcher
Forms/         Help and settings dialogs
Tray/          System tray application context
Program.cs     Entry point
```

## License

Use and modify as you like for personal use.
