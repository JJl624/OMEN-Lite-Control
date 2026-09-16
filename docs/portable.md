# 便携版

完整解压，以管理员身份运行 `OMEN-Lite-Control.exe`，保留 `ec` 和 `driver` 文件夹。
性能模式回读需要 PawnIO；缺失时点击安装按钮，已有兼容驱动直接复用。
没有驱动仍可切换 BIOS 模式和控制键盘，性能状态显示未知。

设置保存在 `data`，首次启动会导入旧语言和预设。升级时保留该目录。
语言切换与颜色编辑不访问硬件；点击应用才写入键盘。
硬件操作间隔至少 1 秒。其他程序修改模式后，点击刷新状态。

## 构建

从 GitHub 下载源码，在 Windows PowerShell 中运行：

```powershell
.\build.ps1
.\tests\run.ps1
.\package.ps1
```

只生成 `OMEN-Lite-Control.exe`。便携包不包含开发源码、测试或构建脚本；第三方源码和许可证保留。
程序不再包含旧 Eco 恢复功能、NVIDIA DLL 或显示刷新率/CPU 电源策略代码。

## 验证范围

仅验证 HP 84DB / BIOS F.19。支持映射、协议测试和实测记录见 [hardware-findings.md](hardware-findings.md)。
驱动安装按钮尚未在无驱动机器上做完整验证；随包官方安装器已在本机成功安装。
