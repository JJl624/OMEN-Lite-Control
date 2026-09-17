# OMEN-Lite-Control

[English](README.md) | [简体中文](README.zh-CN.md)

适用于 **暗影精灵 4（2018），i7-8750H + GTX 1060，SSID 84DB / BIOS F.19** 的性能与键盘灯控制工具。仅在此配置上测试。

<img src="assets/ui-v0.4.1.png" alt="OMEN-Lite-Control 界面" width="75%">

## 功能

- 默认、狂暴、酷冷三种 BIOS 模式，支持 EC 状态回读
- 可点击的四分区键盘，支持颜色与亮度调节
- 灯光预设支持修改、改名、另存和删除
- 中英文界面

## 使用

1. 从 [Releases](https://github.com/JJl624/OMEN-Lite-Control/releases) 下载 ZIP，完整解压，以管理员身份运行 `OMEN-Lite-Control.exe`。
2. 如有提示，点击 **启用硬件读取（安装驱动）**。已附带 [PawnIO 2.2.0 安装包](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)，已有兼容驱动会直接复用；不安装也可切换模式和控制键盘灯。
3. 点击按钮切换性能模式。点击键盘区域选区，双击选色，调整亮度后点击 **应用**；加载预设不会自动写入键盘。

`driver` 文件夹仅存放安装包，安装兼容版本的 PawnIO 后即可删除；需要通过程序安装或更新驱动时再恢复。设置保存在 `data` 中。其他软件修改模式后，点击 **刷新状态**；硬件操作有 1 秒间隔保护。

[更新内容](https://github.com/JJl624/OMEN-Lite-Control/releases) · [许可](LICENSE)

本项目由 Codex 构建。
