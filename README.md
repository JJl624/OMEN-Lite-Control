# OMEN-Lite-Control

A lightweight alternative to the resource-heavy OMEN Gaming Hub, made for the
**HP OMEN 15 (2018 / 暗影精灵 4), i7-8750H + GTX 1060, motherboard SSID 84DB**.

> Beta software. This project is hardware-specific and has only been tested on
> the configuration above. Do not use it on other models.

## Features

- Default, Performance, Cool and Eco modes
- Four-zone static keyboard colors and brightness
- Up to eight custom keyboard-lighting presets
- Chinese and English interface
- No background process, telemetry or automatic startup
- No OMEN Gaming Hub, XTU or HP HSA runtime dependency

## Usage

Download the latest ZIP from [Releases](https://github.com/JJl624/OMEN-Lite-Control/releases),
extract the complete folder, then run `OMEN-Lite-Control.exe` as administrator.

If Eco mode is active, select **Default** before deleting the program to restore
the refresh rate, NVIDIA frame-rate limit and CPU boost settings.

## Notes

- Performance mode display records the last mode selected by this tool because
  this model does not expose hardware readback for that setting.
- Windows may report 143/59 Hz for nominal 144/60 Hz display modes.
- Keep `OmenNvApi.dll` beside the executable.

## 中文

这是为暗影精灵 4（i7-8750H + GTX 1060，主板 SSID 84DB）制作的轻量控制工具。OMEN Gaming Hub 功能太多，而本机只需要性能调节和四分区键盘灯控制功能。解压后请以**管理员身份**运行程序。

本项目仍处于 Beta 阶段，目前只在上述机器上测试。请勿在其他型号上使用。删除前请先点击一次**默认**，以恢复屏幕刷新率、NVIDIA 帧率限制和 CPU 睿频设置。

## License

MIT — see [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
