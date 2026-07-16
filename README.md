# Codex Token Widget

A small portable Windows desktop widget for viewing local Codex token usage.

## What It Does

Codex Token Widget reads local Codex session logs from `~\.codex\sessions` and displays recent token usage in a compact always-on-top desktop widget.

It is designed for users who want a quick desktop-level view of Codex usage without opening a terminal dashboard.

## Features

- Portable single-file Windows executable.
- No Python runtime required on the target machine.
- Always-on-top floating widget.
- Starts in the top-right corner of the primary display.
- Drag the title bar to move the widget.
- Rounded borderless UI with Mac-style close/minimize dots.
- Minimize to the system tray.
- Tray menu with `Show`, `Refresh`, and `Exit`.
- Time ranges: `today`, `24h`, `7d`, `30d`, and `all`.
- Embedded application/tray icon.
- Reads local Codex `token_count.last_token_usage` records.

## Download

Use the latest GitHub Release and download:

```text
CodexTokenWidgetPortable.exe
```

The executable is also stored in this repository under:

```text
dist/CodexTokenWidgetPortable.exe
```

## Usage

1. Download `CodexTokenWidgetPortable.exe`.
2. Double-click it.
3. Use the `today`, `24h`, `7d`, `30d`, and `all` buttons to switch the time range.
4. Click `Refresh` to refresh immediately.
5. Click the yellow dot to minimize to tray.
6. Right-click the tray icon for `Show`, `Refresh`, and `Exit`.

## Data Source

The widget scans local Codex session logs under:

```text
%USERPROFILE%\.codex\sessions
```

It looks for `token_count` events and sums `last_token_usage` values for the selected time range.

The widget does not send data anywhere. It reads local files only.

## Build From Source

Requirements on Windows:

- .NET Framework compiler `csc.exe`, available on standard Windows installations with .NET Framework.

Build command:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo `
  /target:winexe `
  /platform:x64 `
  /optimize+ `
  /win32icon:"assets\CodexTokenWidget.ico" `
  /out:"dist\CodexTokenWidgetPortable.exe" `
  /reference:System.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Drawing.dll `
  "src\CodexTokenWidgetPortable.cs"
```

## Project Structure

```text
.
├── assets/
│   └── CodexTokenWidget.ico
├── dist/
│   └── CodexTokenWidgetPortable.exe
├── src/
│   └── CodexTokenWidgetPortable.cs
├── .gitignore
└── README.md
```

## Notes

- This is a native WinForms executable, not a Python wrapper.
- The target machine needs Windows with .NET Framework 4.x, which is present by default on modern Windows 10/11 systems.
- Token totals depend on the Codex session logs available on the local machine.
