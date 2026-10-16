# NS2 Pro

原生 WinUI 3 手柄连接助手。通过 Windows 蓝牙 LE，将 Switch 2 Pro Controller 的输入桥接为 Xbox 360 / XInput，同时保留原有 NS2 配对。

## 使用

发布版入口：`artifacts/NS2Pro/NS2Pro.exe`。打开后点击「连接手柄」，拔掉 USB，长按手柄顶部 SYNC，直到指示灯循环闪烁。

- **连接总览**：连接 / 断开、报告频率、电量、Xbox 输出状态。
- **手柄测试**：参考官方正面图绘制的原生矢量手柄，按键高亮、摇杆随输入移动；另有摇杆轨迹、轴数值、中心偏移、死区前后比较及数字扳机状态。
- **按键映射**：Nintendo 布局（默认，A→A）和 Xbox 布局（按位置，A→B），配 ABXY 示意图；切换即时生效。支持 0–30% 摇杆死区。
- **设置与诊断**：托盘常驻、启动后等待手柄、指定蓝牙地址、运行环境检查、日志与诊断复制。

窗口关闭后默认留在系统托盘，连接继续运行。托盘右键菜单提供「打开」和「断开并退出」。也可在设置中关闭托盘常驻。

回 NS2：点击「切回 NS2」，然后按手柄 HOME。离线 HOME 仍可唤醒 NS2；进入 PC 模式请使用 SYNC。

手柄测试会在本应用连接后自动开始，输入报告计数持续增长时表示正在检测。若使用旧版控制台，先按 Ctrl+C 结束旧连接，再在原生应用连接并长按 SYNC；两个程序不要同时接管手柄。

## 技术与环境

- C# / .NET 10，WinUI 3，Windows App SDK **2.5.1 stable**。
- Windows 蓝牙 WinRT API；不使用 Electron 或 WebView 页面。
- 预览由 WinUI 的 Path、Ellipse、渐变和变换组成，可独立驱动各个按键及双摇杆；不依赖照片底图、浏览器或三维渲染库。
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

应用运行时可发布到独立目录，避免占用文件和中断游戏：`./scripts/build.ps1 -Publish -OutputDirectory artifacts/NS2Pro-v0.2.1`，再用 `./scripts/install-shortcut.ps1 -AppDirectory artifacts/NS2Pro-v0.2.1` 更新桌面入口。退出原应用后重开即可使用新版。

`tests/NS2Pro.Core.Tests` 为无额外测试框架依赖的协议检查程序，覆盖记录的真实报告、两套映射、校准读命令、广播过滤和 XInput ABI。

`tools/fetch_sdk.py` 可从微软官方源分段下载锁定的 SDK，并按官方 SHA-512 校验；工具不修改系统 PATH。

应用设置和日志保存在 `%LOCALAPPDATA%/NS2Pro`，不进入仓库。SDK、下载包、分析资料、构建输出和 Python 虚拟环境均由 `.gitignore` 排除。NuGet 包通过 `packages.lock.json` 锁定，构建脚本使用锁定还原模式；发布时检查必要资源和许可文件，避免漏掉 WinUI 的 PRI 文件。

## 版本

- `v0.1.0-prototype`：已验证的 Python 无线桥接基线。
- `v0.2.0`：原生 WinUI 界面与 C# 蓝牙桥接，映射预设和手柄测试。
- `v0.2.1`：电量图标、精简总览指标，以及页底游戏输出状态。
- `v0.2.2`：修正页面滚动条位置，避免覆盖内容卡片。

旧版脚本 `Start-PC.cmd` 保留为诊断后备，不要与原生应用同时连接同一只手柄。

## 来源与许可

- 协议：[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research)
- 参考实现：[Switch2Connect](https://github.com/TommyWabg/Switch2Connect)、[switch2controllerpc](https://github.com/CareyScott/switch2controllerpc)
- ViGEmClient 随 [vgamepad 0.1.0](https://pypi.org/project/vgamepad/0.1.0/) 发布；保留许可于 `third_party/vigem/LICENSE.txt`，发布目录也包含该许可。
- 本次原生界面、协议解析与连接服务在项目中实现。REA 未参与实时蓝牙连接。
- 应用图标是本项目生成的简洁手柄图形，源脚本为 `tools/generate_icon.py`。
- 手柄示意图上的 Switch 2 标志为原生矢量重绘，参考任天堂官方图案及 [Wikimedia 矢量标志页面](https://commons.wikimedia.org/wiki/File:Nintendo_Switch_2_logo_transparent_black.svg)，仅用于呈现所连接手柄的外观。

## 三维资源调研

外形以[任天堂官方正面图](https://www.nintendo.com/hk/hardware/switch2/accessories/assets/img/top/controller_switcher_img01.png)为依据：深色主体和握把、浅色肩部、正面中央下方的方形 C 键，以及一体式十字键。

检索 MakerWorld / 拓竹与[创想云](https://www.crealitycloud.com/model-tags/switch-2-pro-controller)后，确认到的相关打印文件主要是支架和收纳盒，尚未找到可直接用作交互预览的免费完整手柄模型。打印 STL 即使外形合适，也还需检查分件、法线、材质和使用授权。

付费候选包括 [CGTrader 的 Switch 2 Pro Controller](https://www.cgtrader.com/3d-models/electronics/computer/pro-controller-2)（2026-10-16 页面标价 US$25，提供 OBJ/FBX/glTF/Blender）与 [TurboSquid 的 NS2 Pro](https://www.turbosquid.com/FullPreview/2405850)。未购买或捆绑；页面格式说明不能证明按键均为独立对象，随应用分发前还需确认授权。

自建合格三维资源需要 Blender 等建模工具、正面/背面/侧面参考、外壳曲面与比例校正、独立按键和摇杆轴心、材质/贴图及多视角验收。当前机器未检测到 Blender；现有正面图不足以精确还原背面和厚度。这些工作明显超出单纯绘制几何体，因此按用户授权采用准确的二维矢量方案。微软的 [XAML Shapes / Geometry](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/shapes) 支持与 SVG 路径兼容的几何绘制及独立变换。

早期三维预览保存在本地提交 `d671f60`，用于保留试验过程，不作为最终外观。
