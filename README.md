# OMEN-Lite-Control

[English](README.md) | [简体中文](README.zh-CN.md)

Performance and keyboard lighting controls for **HP OMEN 15 (2018), i7-8750H + GTX 1060, SSID 84DB / BIOS F.19**. Only tested on this configuration.

<img src="assets/ui-en-v0.4.3.png" alt="OMEN-Lite-Control interface" width="75%">

## Features

- Balanced, Performance and Comfort BIOS modes, with EC state readback
- Clickable four-zone keyboard with color and brightness controls
- Lighting presets with edit, rename, save-as and delete; current BIOS colors stay first
- Chinese / English language switch

## Usage

1. Download the ZIP from [Releases](https://github.com/JJl624/OMEN-Lite-Control/releases), extract all files, and run `OMEN-Lite-Control.exe` as administrator.
2. Click **Enable readback (install driver)** if prompted. The [PawnIO 2.2.0 installer](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0) is included; an existing compatible driver is reused. Mode switching and keyboard lighting work without it.
3. Select a performance mode. For lighting, click a keyboard zone, double-click to choose a color, adjust brightness, then click **Apply**. Loading a preset does not apply it automatically.

The `driver` folder contains only the installer and can be deleted once compatible PawnIO is installed. Restore it only if you need to install or update the driver from the app. Settings are stored only in the app’s `data` folder; no user-profile settings are read or written. Click **Refresh** to reload the mode and keyboard colors; hardware operations have a 1-second cooldown.

[Release notes](https://github.com/JJl624/OMEN-Lite-Control/releases) · [License](LICENSE)

Built with Codex.
