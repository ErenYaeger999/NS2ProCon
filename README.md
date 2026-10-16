# NS2Pro手柄PC连接器

原生 WinUI 3 手柄连接助手。通过 Windows 蓝牙 LE，将 Switch 2 Pro Controller 的输入桥接为 Xbox 360 / XInput，同时保留原有 NS2 配对。

## 使用

优先使用安装包 `artifacts/release/NS2Pro-0.5.2-win-x64-setup.exe`。安装过程离线补齐缺失的官方 ViGEmBus 驱动，创建开始菜单和桌面入口。日常使用无需管理员权限。

新安装首次打开进入「连接指南」：检查 Windows、蓝牙与驱动，拔掉 USB，点击开始连接，再长按手柄顶部 SYNC，直到指示灯循环闪烁。看过指南后，后续冷启动即进入连接总览，无需先完成连接；旧版升级也直接进入总览。仍可随时从左侧打开指南，无需在 Windows 蓝牙设置中手动配对。

同一 Windows 会话只保留一个运行实例，正式模式与引导预览共用限制，不因版本或目录不同而绕过。重复双击仅恢复现有窗口，包括已最小化或隐藏到托盘的窗口；不会创建另一只虚拟手柄或重新接管蓝牙。

- **连接总览**：连接 / 断开、报告频率、电量与充电状态、Xbox 输出状态。电池在 >20% 时为绿色、11–20% 为黄色、≤10% 为红色；正在充电时显示闪电，接电但未充电时不会显示闪电。
- **连接指南**：独立环境检查、缺少驱动时从随包安装器补装、蓝牙设置入口、SYNC 操作步骤与真实输入确认。等待较久时提供排查与发现新手柄入口。
- **手柄测试**：参考官方正面图绘制的原生矢量手柄，按键高亮、摇杆随输入移动；ZL/ZR 与 GL/GR 采用放大的平面按钮图示，背键和 LS/RS 分两行居中。另有摇杆轨迹、轴数值、中心偏移、死区前后比较及数字扳机状态。
- **按键映射**：Nintendo 布局（默认，A→A）和 Xbox 布局（按位置，A→B），配 ABXY 示意图；切换即时生效。支持 0–30% 摇杆死区。
- **设置与诊断**：托盘常驻、启动后等待手柄、指定蓝牙地址、运行环境检查、日志与诊断复制。

侧栏底部统一提供「联系与反馈」「退出应用」图文入口，下方为应用名与版本。「联系与反馈」显示 `945558278@qq.com`，可选中或复制邮箱，也可打开邮件客户端；不会自动发送诊断数据。打开联系面板不会切换当前页面；「退出应用」会结束 PC 连接并完全退出。

运行时右下角系统托盘显示应用图标，右键可连接手柄（之后长按 SYNC）、取消等待、断开 PC 或退出，菜单随连接状态更新。Windows 可能把图标收在「^」溢出区，可从那里拖到任务栏常驻；应用不会修改系统的图标显示偏好。关闭窗口后默认留在托盘，连接继续运行，也可在设置中关闭托盘常驻。

回 NS2：点击「切回 NS2」，然后按手柄 HOME。HOME 会唤醒 NS2；进入 PC 模式请使用 SYNC。页面上方同一条连接提示同时说明 HOME 注意事项和「连接成功后手柄指示灯持续闪烁属正常现象」。提示可以关闭，本次运行不再显示；累计手动关闭三次后永久隐藏，次数保存在本地设置。

手柄测试会在本应用连接后自动开始，输入报告计数持续增长时表示正在检测。若使用旧版控制台，先按 Ctrl+C 结束旧连接，再在原生应用连接并长按 SYNC；两个程序不要同时接管手柄。

## 技术与环境

- C# / .NET 10，WinUI 3，Windows App SDK **2.5.1 stable**。
- Windows 蓝牙 WinRT API；不使用 Electron 或 WebView 页面。
- 预览由 WinUI 的 Path、Ellipse、渐变和变换组成，可独立驱动各个按键及双摇杆；不依赖照片底图、浏览器或三维渲染库。
- ViGEmClient x64 与已有的 ViGEmBus 驱动用于虚拟 Xbox 输出。
- 安装包面向 Intel / AMD x64 Windows 10 2004（19041）及以上和 Windows 11；ARM64、32 位系统不在本版支持范围。本机验证环境为 Windows 11 x64，构建 22631。
- 发布默认自包含 .NET 和 Windows App SDK；无需另装 .NET、Python、WebView2 或启用开发者模式。当前原生 PE 普通与延迟导入检查未发现外置 MSVC 运行库依赖。
- 目标电脑需支持 LE 中央角色的蓝牙适配器、开启蓝牙，以及 ViGEmBus。安装包附带官方 1.22.0 安装器，缺少驱动时自动安装；仅驱动安装需要管理员权限，可能需要重启。已有驱动时跳过安装，卸载主程序时保留共享驱动和个人设置。
- Windows 11 的蓝牙吞吐参数 API 使用能力检测，Windows 10 或不支持该参数的适配器使用默认连接参数，输入频率可能不同。Mica 不可用时使用普通背景；支持 Per-Monitor V2 DPI 与小窗口卡片纵向排列。
- 当前实现按键、摇杆、电量、充电与外接电源状态。充电状态直接取自现有蓝牙输入报告，不新增控制命令；状态只有在收到报告时才能更新。震动、陀螺仪、耳机音频暂未接入；额外按键可测试，暂不映射到 Xbox。

蓝牙程序只接管匹配产品 ID 的 SYNC / 发现广播，忽略重连与唤醒广播。唯一的专有输出命令是读取摇杆校准数据的 `02/04`；不发送配对、闪存写入或主机唤醒命令。

## 构建与验证

不要求安装完整 Visual Studio。需要 .NET SDK **10.0.402**，NuGet 会还原 WinUI 编译工具与 Windows SDK BuildTools。运行解包式应用不要求开启开发者模式。

本项目本地工具位于 `.tools/dotnet`。也可使用系统安装的匹配 SDK：

```powershell
./scripts/build.ps1           # 协议检查 + Debug 构建
./scripts/build.ps1 -Publish  # 协议检查 + 自包含 Release 发布
./scripts/install-shortcut.ps1 # 桌面快捷方式，直接启动原生应用
./scripts/build-release.ps1   # 自包含发布、官方驱动校验、许可收集、安装包与 ZIP
```

应用运行时可发布到独立目录，避免占用文件和中断游戏：`./scripts/build.ps1 -Publish -OutputDirectory artifacts/NS2Pro-v0.2.1`，再用 `./scripts/install-shortcut.ps1 -AppDirectory artifacts/NS2Pro-v0.2.1` 更新桌面入口。退出原应用后重开即可使用新版。

日常修改只运行 `build.ps1` 做检查，或用 `build.ps1 -Publish` 更新可运行目录；不逐次生成安装包和 ZIP。整批修改确认完成、准备推送或发布时，再统一运行 `build-release.ps1`。

`tests/NS2Pro.Core.Tests` 为无额外测试框架依赖的检查程序，覆盖记录的真实报告、两套映射、校准读命令、广播过滤、XInput ABI、旧设置迁移和首次指南路由。Windows 上通过隔离名称的真实子进程验证并发启动、启动期激活、旧预览互斥、退出与崩溃后的锁恢复，不连接手柄或影响正在运行的应用。

发布脚本固定驱动和编译器版本、SHA-256，并检查厂商数字签名；编译器通过已签名的 NuGet 工具包解压使用，不安装到系统。安装程序由 Inno Setup 6.7.3 构建，中文翻译来自其官方仓库的 `Files/Languages/ChineseSimplified.isl`，保留译者署名。驱动来源和摘要位于 `installer/prerequisites.json`，工具链位于 `installer/toolchain.json`。

`scripts/check-release.ps1 -PublishDirectory artifacts/NS2Pro-v0.5.2-release` 检查运行库、PRI 和图标资源、厂商安装器签名、版本和全文件哈希；结果位于 `artifacts/release/NS2Pro-0.5.2-check-report.json`。`tools/audit_pe_imports.py` 使用 Python 标准库静态读取普通及延迟导入表，不加载或执行被检查的 DLL。构建安装包的开发电脑需要 Python 3 运行此审计；用户电脑无需 Python。

发布前仍需在干净 Windows 10 / Windows 11 x64 电脑验证首次安装、无驱动安装、重启、首次蓝牙连接和卸载。目前本机已有 ViGEmBus，不能以本机通过代替这部分验证；不声明已在其他蓝牙芯片、Windows 10、ARM64 或精简系统实测通过。程序和安装包尚未配置发行者代码签名，首次下载运行可能被 Windows SmartScreen 提示；正式广泛分发前应配置签名。

开发检查首次界面可用 `NS2Pro.exe --preview-onboarding`：使用独立设置和日志，禁止连接真实手柄与安装驱动，但与正式模式共用单实例限制，需先退出正式模式。预览每次仍显示指南，该参数不会加入正常桌面快捷方式。

`tools/fetch_sdk.py` 可从微软官方源分段下载锁定的 SDK，并按官方 SHA-512 校验；工具不修改系统 PATH。

应用设置和日志保存在 `%LOCALAPPDATA%/NS2Pro`，不进入仓库。SDK、下载包、分析资料、构建输出和 Python 虚拟环境均由 `.gitignore` 排除。NuGet 包通过 `packages.lock.json` 锁定，构建脚本使用锁定还原模式；发布时检查必要资源和许可文件，避免漏掉 WinUI 的 PRI 文件。

## 版本

- `v0.1.0-prototype`：已验证的 Python 无线桥接基线。
- `v0.2.0`：原生 WinUI 界面与 C# 蓝牙桥接，映射预设和手柄测试。
- `v0.2.1`：电量图标、精简总览指标，以及页底游戏输出状态。
- `v0.2.2`：修正页面滚动条位置，避免覆盖内容卡片。
- `v0.3.0`：联系入口、首次连接指南、系统兼容处理、离线驱动安装包和发布检查。
- `v0.3.1`：手柄模型加减号改用清晰的填充式矢量符号。
- `v0.4.0`：红白主题与统一手柄图标，小尺寸图标单独优化。
- `v0.5.0`：扳机和背键图示、底部两行居中、HOME 提醒、充电状态与托盘快捷连接。
- `v0.5.1`：统一侧栏底部操作与版本信息，完善联系面板与退出反馈。
- `v0.5.2`：收紧单实例、启动期激活与崩溃恢复，移除启动占位文案，指南仅首次出现。

旧版脚本 `Start-PC.cmd` 保留为诊断后备，不要与原生应用同时连接同一只手柄。

## 来源与许可

- 协议：[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research)；充电与外接电源状态对应 [Pro Controller 2 输入报告 Power Info](https://github.com/ndeadly/switch2_controller_research/blob/master/hid_reports.md#input-report-0x09) 的 bit 1 与 bit 0。
- 参考实现：[Switch2Connect](https://github.com/TommyWabg/Switch2Connect)、[switch2controllerpc](https://github.com/CareyScott/switch2controllerpc)
- ViGEmClient 随 [vgamepad 0.1.0](https://pypi.org/project/vgamepad/0.1.0/) 发布；保留许可于 `third_party/vigem/LICENSE.txt`，发布目录也包含该许可。
- ViGEmBus 使用 [官方 1.22.0 发布](https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0)的完整签名安装器，保留 BSD-3-Clause 许可。上游已归档；该版本移除了自动更新器，安装包不会添加自定义驱动或关闭系统安全机制。
- 本次原生界面、协议解析与连接服务在项目中实现。REA 未参与实时蓝牙连接。
- 主题红色 #E60012 取自[任天堂官网样式](https://www.nintendo.com/jp/common/v3/css/shared.css)。应用图标为本项目原创的红白手柄与四格按键图形，源文件为 `src/NS2Pro.App/Assets/App.svg`；`tools/generate_icon.py` 使用 Pillow 导出 PNG 与多尺寸 ICO，小尺寸按像素单独调整。
- 手柄示意图上的 Switch 2 标志为原生矢量重绘，参考任天堂官方图案及 [Wikimedia 矢量标志页面](https://commons.wikimedia.org/wiki/File:Nintendo_Switch_2_logo_transparent_black.svg)，仅用于呈现所连接手柄的外观。

## 三维资源调研

外形以[任天堂官方正面图](https://www.nintendo.com/hk/hardware/switch2/accessories/assets/img/top/controller_switcher_img01.png)为依据：深色主体和握把、浅色肩部、正面中央下方的方形 C 键，以及一体式十字键。

检索 MakerWorld / 拓竹与[创想云](https://www.crealitycloud.com/model-tags/switch-2-pro-controller)后，确认到的相关打印文件主要是支架和收纳盒，尚未找到可直接用作交互预览的免费完整手柄模型。打印 STL 即使外形合适，也还需检查分件、法线、材质和使用授权。

付费候选包括 [CGTrader 的 Switch 2 Pro Controller](https://www.cgtrader.com/3d-models/electronics/computer/pro-controller-2)（2026-10-16 页面标价 US$25，提供 OBJ/FBX/glTF/Blender）与 [TurboSquid 的 NS2 Pro](https://www.turbosquid.com/FullPreview/2405850)。未购买或捆绑；页面格式说明不能证明按键均为独立对象，随应用分发前还需确认授权。

自建合格三维资源需要 Blender 等建模工具、正面/背面/侧面参考、外壳曲面与比例校正、独立按键和摇杆轴心、材质/贴图及多视角验收。当前机器未检测到 Blender；现有正面图不足以精确还原背面和厚度。这些工作明显超出单纯绘制几何体，因此按用户授权采用准确的二维矢量方案。微软的 [XAML Shapes / Geometry](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/shapes) 支持与 SVG 路径兼容的几何绘制及独立变换。

早期三维预览保存在本地提交 `d671f60`，用于保留试验过程，不作为最终外观。
