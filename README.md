# NS2Pro手柄PC连接器

通过蓝牙把 Nintendo Switch 2 Pro Controller 无线连接到 Windows，输出为 Xbox 360 / XInput 手柄，并保留原有 NS2 配对。

## 功能

- **无线连接**：使用顶部 SYNC 连接 PC；断开后可按 HOME 回到 NS2，无需重新配对。
- **实时测试**：矢量手柄按键高亮、双摇杆轨迹、轴数值、中心偏移和数字扳机状态。
- **两套映射**：默认字母对应（A → A），可切换为 Xbox 位置对应（A → B）；支持 0–30% 摇杆死区，修改即时生效。
- **电量与充电**：电池图标随电量变色，充电时显示闪电。
- **托盘操作**：右键连接、取消等待、断开或退出；单实例运行，重复打开会恢复现有窗口。

当前支持按键、摇杆、电量和充电状态。HOME、截图、C、GL / GR 可检测，暂未映射到 Xbox；震动、陀螺仪和耳机音频暂未支持。

## 系统要求与安装

- Intel / AMD **x64** 的 Windows 10 2004（19041）及以上，或 Windows 11。
- 支持 **蓝牙 LE 中央角色**的适配器，并开启蓝牙。
- 安装版自带 .NET、WinUI 运行环境和官方 **ViGEmBus 1.22.0** 离线安装器，无需另装 .NET、Python、WebView2 或启用开发者模式。

运行 `NS2Pro-版本-win-x64-setup.exe` 完成安装，再打开桌面或开始菜单入口。安装程序需要管理员权限，已有 ViGEmBus 时跳过驱动安装；若提示重启，请重启后再连接。日常使用无需管理员权限。

目前已在本机 Windows 11 x64 验证。Windows 10 干净系统首次安装、无驱动安装仍待实机验证；暂不支持 ARM64 和 32 位系统。发行者代码签名尚未配置。

## 使用方法

1. 拔掉手柄 USB 线，打开应用；首次进入按「连接指南」检查环境。
2. 点击「连接手柄」，再长按顶部 **SYNC** 小键，直到指示灯循环闪烁。无需在 Windows 蓝牙设置中手动配对。
3. 状态显示已连接后，即可开始游戏，或进入「手柄测试」检查按键和摇杆。
4. 回到 NS2 时，先点击「断开 PC 连接」，再按手柄 **HOME**。

**连接 PC 时请勿按 HOME，它会唤醒旁边的 NS2。连接成功后，手柄指示灯持续闪烁属于正常现象。**

点窗口关闭按钮默认断开并退出；最小化可保持连接。需要关闭窗口后继续连接时，在设置中开启后台运行，再从托盘菜单完全退出。Windows 可能把托盘图标收在右下角「^」中。

连接失败时点击「重试连接」；一直找不到手柄时，可在指南中检查 USB、其他连接工具，以及是否需要发现新手柄。不要让旧版控制台或其他桥接程序同时接管同一只手柄。

## 空间占用

当前精简构建的文件总大小如下（**MiB = 1,048,576 字节**，不是运行时内存占用）：

| 内容 | 大小 |
| --- | ---: |
| 完整发布目录，含运行环境、离线驱动和许可 | **约 177.0 MiB** |
| 其中，程序 EXE 与两个业务 DLL | 约 0.63 MiB |
| 其中，ViGEmBus 离线安装器 | 约 6.0 MiB |
| 精简前完整发布目录 | 约 237.5 MiB |

改为按需引用 Windows App SDK 组件后，移除了未使用的 AI、ONNX、DirectML、搜索和小组件依赖，目录体积减少约 **60.5 MiB（25.5%）**。仍自带完整 .NET 和所需 WinUI 运行环境。

以上不含安装到 Windows 的共享驱动、个人日志和安装时的临时文件，实际磁盘分配会略有差异。精简版尚未生成新安装包；此前约 65.3 MiB 的旧安装包不代表精简后的下载大小，新值随下次统一打包更新。

## 开发

使用 C# / .NET 10 与原生 WinUI 3，依赖版本通过 NuGet 锁文件固定。需要 .NET SDK **10.0.402**；本地 SDK 可放在 `.tools/dotnet`，也可使用系统 SDK。

```powershell
./scripts/build.ps1                                      # 检查并编译
./scripts/build.ps1 -Publish -OutputDirectory artifacts/dev-new  # 发布到全新目录
./scripts/build-release.ps1 -OutputDirectory artifacts/release-new -SkipPackaging
# 上一条补齐离线驱动和许可、检查完整目录，但不生成安装包或 ZIP
./scripts/install-shortcut.ps1 -AppDirectory artifacts/release-new
```

发布完整安装包时运行 `scripts/build-release.ps1`。日常修改不逐次打包；变更记录见 [CHANGELOG](CHANGELOG.md)。发布检查报告记录文件哈希、运行环境自包含状态及完整目录大小。依赖调整后请使用全新输出目录，避免混入旧 DLL。

设置和日志保存在 `%LOCALAPPDATA%/NS2Pro`；SDK、下载包及构建产物不进入仓库。卸载应用会保留个人设置和共享 ViGEmBus 驱动。

## 来源与致谢

感谢以下作者和项目提供的协议研究、实现参考与基础组件：

| 作者 / 项目 | 本项目中的用途 |
| --- | --- |
| [ndeadly / switch2_controller_research](https://github.com/ndeadly/switch2_controller_research) | 广播、输入报告、校准读取及电源状态的协议资料 |
| [TommyWabg / Switch2Connect](https://github.com/TommyWabg/Switch2Connect) | 无线连接与 Xbox 桥接的实现参考 |
| [CareyScott / switch2controllerpc](https://github.com/CareyScott/switch2controllerpc) | 手柄连接与输入桥接的实现参考 |
| [Benjamin Höglinger-Stelzer / Nefarius：ViGEmBus](https://github.com/nefarius/ViGEmBus)、[ViGEmClient](https://github.com/nefarius/ViGEmClient) | 虚拟 Xbox 360 手柄驱动与客户端；ViGEmBus 使用官方 1.22.0 签名安装器 |
| [Yann Bouteiller / vgamepad](https://github.com/yannbouteiller/vgamepad) | 本项目随包的 ViGEmClient DLL 来自 vgamepad 0.1.0 发布文件，保留其许可 |
| [Microsoft / Windows App SDK](https://github.com/microsoft/WindowsAppSDK)、[.NET](https://github.com/dotnet/runtime) | 原生界面、Windows 接口与运行环境 |

手柄示意图参考[任天堂官方正面图](https://www.nintendo.com/hk/hardware/switch2/accessories/assets/img/top/controller_switcher_img01.png)，Switch 2 标志参考 [Wikimedia 矢量资源](https://commons.wikimedia.org/wiki/File:Nintendo_Switch_2_logo_transparent_black.svg)；主题红色 `#E60012` 参考[任天堂官网样式](https://www.nintendo.com/jp/common/v3/css/shared.css)。应用图标为本项目原创，见 `src/NS2Pro.App/Assets/App.svg`。

随包第三方许可保存在 `Licenses`，仓库保留于 `third_party` 及依赖包中。本项目为独立工具，与任天堂、微软无隶属关系。

## 联系与反馈

应用左下角「联系与反馈」，或发送邮件至 **945558278@qq.com**。
