# OMEN-Lite-Control

[English](README.md) | [简体中文](README.zh-CN.md)

A lightweight alternative to OMEN Gaming Hub, made for the **HP OMEN 15 (2018),
i7-8750H + GTX 1060, motherboard SSID 84DB / BIOS F.19**.

> Hardware-specific. Only tested on the configuration above.

<img src="assets/ui-en.png" alt="OMEN-Lite-Control interface" width="100%">

## Features

- Default, Performance and Cool BIOS modes
- Actual performance state readback through EC
- Four-zone static keyboard colors and brightness
- Up to eight keyboard-lighting presets
- Chinese and English interface
- No OMEN Gaming Hub, NVIDIA DLL or OmenMon dependency

## Usage

1. Download the portable ZIP from [Releases](https://github.com/JJl624/OMEN-Lite-Control/releases), extract the complete folder, then run `OMEN-Lite-Control.exe` as administrator.
2. For hardware state readback, click **Enable readback (install driver)** once. The bundled PawnIO installer needs no separate download; an existing compatible driver is reused.
3. Select a performance mode. For keyboard lighting, choose colors and brightness, then click **Apply**. Loading a preset does not apply it automatically.

## Notes

- Keep the `ec` and `driver` folders beside the executable. Settings are saved in `data`.
- Without PawnIO, mode switching and keyboard controls still work; performance state shows as unknown.
- Click **Refresh** after changing modes in another app. Hardware actions have a 1-second cooldown; language switching and color editing remain available.
- Eco was a software combination, not a separate BIOS mode, and has been removed. Old settings can be restored when backups exist; NVIDIA recovery requires the old `OmenNvApi.dll`.

[Build and driver details](docs/portable.md) · [v0.3.0 release notes](docs/releases/v0.3.0.md)

## License

MIT — see [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
