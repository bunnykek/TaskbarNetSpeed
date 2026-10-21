# TaskbarNetSpeed

A tiny live internet speed monitor for the Windows 11 taskbar. It shows your current upload and download speed on a transparent background, right next to the system tray.

```
↑ 17.0 KB/s
↓  262 KB/s
```

- About 17 KB, a single file, nothing to install
- Updates once a second
- Counts all active internet adapters together and skips virtual ones (Hyper-V, WSL), so traffic isn't counted twice
- White text on a dark taskbar, black on a light one; follows theme changes
- Stays above the taskbar, and hides during full-screen games and videos

## Usage

Download the build for your PC and run it:

| File | For |
| --- | --- |
| `TaskbarNetSpeed-x64.exe` | Most Windows PCs (Intel / AMD, 64-bit) |
| `TaskbarNetSpeed-x86.exe` | 32-bit Windows |
| `TaskbarNetSpeed-arm64.exe` | ARM PCs (Snapdragon, etc.) |

Not sure? Use **x64**.

- **Right-click** the text to:
  - choose a speed unit: Auto (KB/s, MB/s), KB/s, MB/s, Auto (Kbps, Mbps), Kbps or Mbps
  - turn **Start with Windows** on or off
  - **Reset position**
  - **Exit**
- **Left-drag** the text to move it along the taskbar.

Your unit and position are saved in `%APPDATA%\TaskbarNetSpeed\settings.txt`.

> If you move the .exe after turning on Start with Windows, turn the setting off and on again so it points to the new location.

## Build

No SDK or Visual Studio is needed. Windows ships with the .NET Framework C# compiler, so just run:

```bat
build.bat
```

This puts `TaskbarNetSpeed-x64.exe`, `TaskbarNetSpeed-x86.exe` and `TaskbarNetSpeed-arm64.exe` in the `bin` folder.

The built-in compiler can't target ARM64 directly, so the arm64 file is an AnyCPU build. It runs natively on ARM64 Windows 11, which includes .NET Framework 4.8.1 with ARM64 support.

## Versioning

The current version is in the [`VERSION`](VERSION) file, and every release is listed in [`CHANGELOG.md`](CHANGELOG.md). Versions follow [Semantic Versioning](https://semver.org). The version is shown in the right-click menu and in the .exe's file properties.

## Requirements

- Windows 10 or 11 with .NET Framework 4.x (built into Windows)

## Uninstall

1. Right-click the text, untick **Start with Windows**, then choose **Exit**.
2. Delete the .exe and the `%APPDATA%\TaskbarNetSpeed` folder.
