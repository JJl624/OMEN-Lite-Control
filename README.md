# OMEN-Lite-Control

> Beta software for **HP OMEN 15-dc0xxx with motherboard SSID 84DB only**.

A lightweight control tool for the HP OMEN 15-dc0xxx (84DB), providing
performance modes, power-saving features, and four-zone keyboard lighting
without running OMEN Gaming Hub.

## Features

- Default, Performance and Cool BIOS thermal policies.
- Eco mode with an approximately 60 Hz display mode, NVIDIA 60 FPS limit and
  disabled CPU boost; previous values are restored when leaving Eco mode.
- Independent static RGB color and intensity controls for four keyboard zones.
- No background process, telemetry or automatic startup.

## Important limitations

- This build is hardware-specific. Do **not** use it on another motherboard.
- The displayed performance mode is the last mode written by this tool. HP's
  `0x1A` WMI command is write-only on this platform, so it is not a hardware
  readback.
- Display refresh rate is read live from Windows. Driver rounding may report
  143 Hz for a nominal 144 Hz mode, or 59 Hz for a nominal 60 Hz mode.
- Per-zone brightness is implemented by scaling RGB intensity. It does not
  replace the keyboard's global hardware backlight level.
- Administrator privileges are required for HP BIOS WMI writes.

## Requirements

- HP OMEN 15-dc0xxx, motherboard SSID `84DB`.
- 64-bit Windows 10 or Windows 11 with .NET Framework 4.x.
- NVIDIA display driver for the Eco-mode frame-rate limiter.

OMEN Gaming Hub, XTU, HP HSA services and `HpReadHWData.sys` are not required at
runtime.

## Usage

Download the latest ZIP from Releases, extract the complete folder, and run
`OMEN-Lite-Control.exe`. Keep `OmenNvApi.dll` beside the executable.

Before shutting down or removing the tool while Eco mode is active, click
**Default** once so the saved refresh-rate, frame-rate and CPU-boost settings
can be restored.

## Building

Run from an x64 Visual Studio Developer PowerShell:

```powershell
./scripts/build.ps1
```

The script downloads the official NVIDIA NVAPI SDK and writes the build to
`dist`. GitHub Actions uses the same script for tagged prereleases.

## 中文说明

OMEN-Lite-Control 是为 **HP OMEN 15-dc0xxx（主板 SSID 84DB）**制作的轻量控制工具，
无需运行 OMEN Gaming Hub，即可切换默认、狂暴、酷冷和节能模式，并控制四分区键盘
静态颜色与 RGB 强度。

本项目目前处于 Beta 阶段，只允许在上述型号使用。节能状态下退出或删除程序前，请先
点击一次“默认”，以恢复刷新率、NVIDIA 限帧和 CPU 睿频设置。

## License

MIT. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
