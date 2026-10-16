# NS2 Pro

原生 WinUI 3 手柄连接助手。通过 Windows 蓝牙 LE，将 Switch 2 Pro Controller 的输入桥接为 Xbox 360 / XInput，同时保留原有 NS2 配对。

## 使用

发布版入口：`artifacts/NS2Pro/NS2Pro.exe`。打开后点击「连接手柄」，拔掉 USB，长按手柄顶部 SYNC，直到指示灯循环闪烁。

- **连接总览**：连接 / 断开、报告频率、电量、Xbox 输出状态。
- **手柄测试**：真正的三维网格手柄，可旋转、缩放、看背面，按键下压高亮、摇杆倾斜；另有按键指示灯、摇杆轨迹、轴数值、中心偏移、死区前后比较及数字扳机状态。
- **按键映射**：Nintendo 布局（默认，A→A）和 Xbox 布局（按位置，A→B），配 ABXY 示意图；切换即时生效。支持 0–30% 摇杆死区。
- **设置与诊断**：托盘常驻、启动后等待手柄、指定蓝牙地址、运行环境检查、日志与诊断复制。

窗口关闭后默认留在系统托盘，连接继续运行。托盘右键菜单提供「打开」和「断开并退出」。也可在设置中关闭托盘常驻。

回 NS2：点击「切回 NS2」，然后按手柄 HOME。离线 HOME 仍可唤醒 NS2；进入 PC 模式请使用 SYNC。

手柄测试会在本应用连接后自动开始，输入报告计数持续增长时表示正在检测。若使用旧版控制台，先按 Ctrl+C 结束旧连接，再在原生应用连接并长按 SYNC；两个程序不要同时接管手柄。

## 技术与环境

- C# / .NET 10，WinUI 3，Windows App SDK **2.5.1 stable**。
- Windows 蓝牙 WinRT API；不使用 Electron 或 WebView 页面。
- 三维渲染使用 HelixToolkit.WinUI.SharpDX **3.1.2**（原生 DirectX 11）。模型由项目代码生成，是按 NS2 Pro 特征绘制的三维示意模型，不是任天堂精确 CAD。
- ViGEmClient x64 与已有的 ViGEmBus 驱动用于虚拟 Xbox 输出。
- Windows 最低构建 19041；本机验证环境为 Windows 11 x64，构建 22631。
- 发布默认自包含 .NET 和 Windows App SDK。目标电脑仍需 BLE 适配器及 ViGEmBus 驱动。
- 当前实现按键、摇杆与电量。震动、陀螺仪、耳机音频暂未接入；额外按键可测试，暂不映射到 Xbox。

蓝牙程序只接管匹配产品 ID 的 SYNC / 发现广播，忽略重连与唤醒广播。唯一的专有输出命令是读取摇杆校准数据的 `02/04`；不发送配对、闪存写入或主机唤醒命令。

## 构建与验证

不要求安装完整 Visual Studio。需要 .NET SDK **10.0.402**，NuGet 会还原 WinUI 编译工具与 Windows SDK BuildTools。运行解包式应用不要求开启开发者模式。

本项目本地工具位于 `.tools/dotnet`。也可使用系统安装的匹配 SDK：

```powershell
./scripts/build.ps1           # 协议检查 + Debug 构建
./scripts/build.ps1 -Publish  # 协议检查 + 自包含 Release 发布
./scripts/install-shortcut.ps1 # 桌面快捷方式，直接启动原生应用
```

`tests/NS2Pro.Core.Tests` 为无额外测试框架依赖的协议检查程序，覆盖记录的真实报告、两套映射、校准读命令、广播过滤和 XInput ABI。

`tools/fetch_sdk.py` 可从微软官方源分段下载锁定的 SDK，并按官方 SHA-512 校验；工具不修改系统 PATH。

应用设置和日志保存在 `%LOCALAPPDATA%/NS2Pro`，不进入仓库。SDK、下载包、分析资料、构建输出和 Python 虚拟环境均由 `.gitignore` 排除。NuGet 包通过 `packages.lock.json` 锁定，构建脚本使用锁定还原模式；发布时检查必要资源和许可文件，避免漏掉 WinUI 的 PRI 文件。

## 版本

- `v0.1.0-prototype`：已验证的 Python 无线桥接基线。
- `v0.2.0`：原生 WinUI 界面与 C# 蓝牙桥接，映射预设和手柄测试。

旧版脚本 `Start-PC.cmd` 保留为诊断后备，不要与原生应用同时连接同一只手柄。

## 来源与许可

- 协议：[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research)
- 参考实现：[Switch2Connect](https://github.com/TommyWabg/Switch2Connect)、[switch2controllerpc](https://github.com/CareyScott/switch2controllerpc)
- ViGEmClient 随 [vgamepad 0.1.0](https://pypi.org/project/vgamepad/0.1.0/) 发布；保留许可于 `third_party/vigem/LICENSE.txt`，发布目录也包含该许可。
- 本次原生界面、协议解析与连接服务在项目中实现。REA 未参与实时蓝牙连接。
- [Helix Toolkit](https://github.com/helix-toolkit/helix-toolkit)：MIT 许可，三维网格、材质、光照和交互相机；许可随发布文件保存。
- 应用图标是本项目生成的简洁手柄图形，源脚本为 `tools/generate_icon.py`。

## 三维资源调研

查到 [CGTrader 的 Switch 2 Pro Controller](https://www.cgtrader.com/3d-models/electronics/computer/pro-controller-2) 和 [TurboSquid 的 NS2 Pro](https://www.turbosquid.com/FullPreview/2405850) 等付费成品。未购买或捆绑这些资源；搜索到的免费老款 Switch Pro 模型不能当作 NS2 Pro 使用。当前采用自建示意模型，各个输入部件独立，便于实时驱动。
