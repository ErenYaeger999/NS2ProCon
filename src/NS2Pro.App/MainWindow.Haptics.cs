using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NS2Pro.Core;
using NS2Pro.App.Services;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private readonly List<Button> rumbleTests = new();
    private TextBlock? rumbleStatus, rumbleTelemetry;
    private Button? rumbleStop;

    private UIElement RumbleTestCard()
    {
        var content = new StackPanel { Spacing = 13 };
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(Text("震动测试", 17, true));
        rumbleStop = new Button { Content = "立即停止" };
        rumbleStop.Click += (_, _) => controller.StopRumble();
        Grid.SetColumn(rumbleStop, 1); heading.Children.Add(rumbleStop); content.Children.Add(heading);
        content.Children.Add(Text("轻握手柄，选择一种效果。每次测试会自动停止；测试期间暂停接收游戏震动。", 12, false, AppPalette.Muted));
        var buttons = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 6, ItemWidth = 104, ItemHeight = 46 };
        foreach (var (label, pattern) in new[] { ("低频", HapticTest.Low), ("高频", HapticTest.High),
            ("左侧", HapticTest.Left), ("右侧", HapticTest.Right), ("轻点", HapticTest.Click), ("连续短脉冲", HapticTest.Pulses) }) {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 8, 8) };
            button.Click += (_, _) => { controller.TestRumble(pattern); UpdateRumbleStatus(); };
            rumbleTests.Add(button); buttons.Children.Add(button);
        }
        content.Children.Add(buttons);
        rumbleStatus = Text("连接手柄后可测试震动", 12, false, AppPalette.Muted);
        rumbleTelemetry = Text("", 11, false, AppPalette.Muted);
        content.Children.Add(rumbleStatus); content.Children.Add(rumbleTelemetry);
        return Card(content, 22);
    }

    private UIElement RumbleSettingsCard()
    {
        var content = new StackPanel { Spacing = 14, Children = { Text("手柄震动", 17, true) } };
        var toggle = new ToggleSwitch { Header = "游戏震动", IsOn = settings.RumbleEnabled,
            OnContent = "跟随游戏反馈", OffContent = "已关闭" };
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 5,
            Header = $"震动强度 · {settings.RumbleStrength:P0}", Value = settings.RumbleStrength * 100,
            IsEnabled = settings.RumbleEnabled };
        toggle.Toggled += (_, _) => {
            settings.RumbleEnabled = toggle.IsOn; slider.IsEnabled = toggle.IsOn;
            controller.ConfigureRumble(); SaveSettings(); UpdateRumbleStatus();
        };
        slider.ValueChanged += (_, args) => {
            settings.RumbleStrength = args.NewValue / 100;
            slider.Header = $"震动强度 · {settings.RumbleStrength:P0}";
            controller.ConfigureRumble(); SaveSettings(); UpdateRumbleStatus();
        };
        content.Children.Add(toggle); content.Children.Add(slider);
        content.Children.Add(Text("将游戏的低频、高频反馈转换为 NS2 Pro 震动。可到手柄测试页体验；具体效果由游戏提供的反馈决定。", 12, false, AppPalette.Muted));
        return Card(content, 24);
    }

    private bool wantsEnhanced, preparingEnhancement, changingBackendSettings, updatingBackendOptions;
    private ComboBox? gameMode;
    private StackPanel? enhancementPanel;
    private TextBlock? enhancementMessage, enhancementDetail, enhancementGuide, modeHint, installHint, backendManagementHint;
    private Expander? enhancementIssue;
    private Button? enhancementAction, enhancementCancel, useXbox, recheckEnhancement;
    private ProgressBar? enhancementProgress;
    private ToggleSwitch? externalBackend;
    private NumberBox? backendPort;
    private EnhancementStatus? lastEnhancementStatus;
    private bool lastEnhancementActive, lastNativeFeedback;

    private UIElement HapticBackendOptions()
    {
        var content = new StackPanel { Spacing = 14, Children = { Text("游戏模式", 17, true) } };
        gameMode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = -1 };
        gameMode.Items.Add("通用兼容（推荐）"); gameMode.Items.Add("原生触觉增强（实验）");
        gameMode.SelectedIndex = wantsEnhanced ? 1 : 0;
        content.Children.Add(gameMode);
        modeHint = Text("在启动游戏前选择。切换模式前请先断开 PC 连接。", 12, false, AppPalette.Muted);
        content.Children.Add(modeHint);
        enhancementPanel = new StackPanel { Spacing = 14 };
        enhancementPanel.Children.Add(Text("通过虚拟 DualSense 接收支持游戏的原生触觉，并适配为 NS2 Pro 震动。具体游戏是否改善震感尚无法确认，需实际体验后选择。", 13));
        backendManagementHint = Text("", 12, false, AppPalette.Muted); enhancementPanel.Children.Add(backendManagementHint);
        enhancementMessage = Text("", 14, true);
        enhancementProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4 };
        enhancementPanel.Children.Add(enhancementMessage); enhancementPanel.Children.Add(enhancementProgress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        enhancementAction = new Button { Content = "安装增强组件 · 约 37 MB", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        enhancementCancel = new Button { Content = "取消" };
        useXbox = new Button { Content = "使用通用模式" };
        enhancementAction.Click += async (_, _) => await RunEnhancementActionAsync();
        enhancementCancel.Click += (_, _) => enhancement.Cancel();
        useXbox.Click += async (_, _) => await SelectGameModeAsync(false);
        actions.Children.Add(enhancementAction); actions.Children.Add(enhancementCancel); actions.Children.Add(useXbox);
        enhancementPanel.Children.Add(actions);
        installHint = Text("按需下载并校验官方组件。安装驱动需确认 Windows 管理员弹窗，可能短暂重连 USB 设备；请先完成文件传输。安装后需重启电脑，应用不会自动重启。", 12, false, AppPalette.Muted);
        enhancementPanel.Children.Add(installHint);
        enhancementGuide = Text("", 12, false, AppPalette.Muted);
        enhancementPanel.Children.Add(enhancementGuide);
        enhancementDetail = Text("", 12, false, AppPalette.Muted);
        enhancementIssue = new Expander { Header = "查看问题详情", Content = enhancementDetail,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        enhancementPanel.Children.Add(enhancementIssue);
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(Text($"自动管理：VIIPER {EnhancementComponents.BackendVersion} + USBIP {EnhancementComponents.DriverVersion}。已有其他版本时不会自动降级共享驱动。", 12, false, AppPalette.Muted));
        externalBackend = new ToggleSwitch { Header = "连接其他软件的后端（手动）", IsOn = !settings.ManageHapticBackend,
            OnContent = "需要先在其他软件中启动 VIIPER", OffContent = "本应用自动启动后端（推荐）" };
        backendPort = new NumberBox { Header = "已有后端的本机端口", Minimum = 1024, Maximum = 65535,
            Value = settings.ViiperPort, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        externalBackend.Toggled += async (_, _) => {
            if (!updatingBackendOptions) await ChangeBackendSettingsAsync(!externalBackend.IsOn, settings.ViiperPort, checkAfterChange: true);
        };
        backendPort.ValueChanged += async (_, args) => {
            if (updatingBackendOptions || !double.IsFinite(args.NewValue)) return;
            await ChangeBackendSettingsAsync(settings.ManageHapticBackend, (int)Math.Clamp(args.NewValue, 1024, 65535), checkAfterChange: false);
        };
        advanced.Children.Add(externalBackend); advanced.Children.Add(backendPort);
        recheckEnhancement = new Button { Content = "重新检测（不安装）" };
        recheckEnhancement.Click += async (_, _) => await PrepareEnhancementAsync(recheck: true);
        advanced.Children.Add(recheckEnhancement);
        advanced.Children.Add(Text("已有后端需支持 VPCM V5，并由原软件负责启动。本应用只连接本机，不接管其他软件的进程或设备。", 12, false, AppPalette.Muted));
        advanced.Children.Add(Text("自动管理会复用已校验的组件，只有确认缺少时才提供安装入口；检测和切换管理方式不会下载或安装驱动。", 12, false, AppPalette.Muted));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        links.Children.Add(new HyperlinkButton { Content = "VIIPER · GPL-3.0", NavigateUri = new(EnhancementComponents.BackendSource) });
        links.Children.Add(new HyperlinkButton { Content = "USBIP · 来源与许可", NavigateUri = new(EnhancementComponents.DriverSource) });
        advanced.Children.Add(links);
        enhancementPanel.Children.Add(new Expander { Header = "高级选项与组件来源", Content = advanced,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        content.Children.Add(enhancementPanel);
        gameMode.SelectionChanged += async (_, _) => {
            if (gameMode.SelectedIndex >= 0 && (gameMode.SelectedIndex == 1) != wantsEnhanced)
                await SelectGameModeAsync(gameMode.SelectedIndex == 1);
        };
        lastEnhancementStatus = null; UpdateEnhancementUi();
        bool checkedOnLoad = false;
        content.Loaded += async (_, _) => {
            if (checkedOnLoad || !wantsEnhanced || AppSettings.IsPreview || controller.Info.IsActive) return;
            checkedOnLoad = true; await PrepareEnhancementAsync();
        };
        return Card(content, 24);
    }

    private async Task SelectGameModeAsync(bool enhanced)
    {
        if (exiting || startingConnection || controller.Info.IsActive || enhancement.Status.Busy || preparingEnhancement || changingBackendSettings) {
            if (gameMode is not null) gameMode.SelectedIndex = wantsEnhanced ? 1 : 0;
            return;
        }
        wantsEnhanced = enhanced;
        settings.PreferNativeHaptics = enhanced;
        settings.OutputMode = GameOutputMode.Xbox; SaveSettings();
        if (gameMode is not null) gameMode.SelectedIndex = enhanced ? 1 : 0;
        lastEnhancementStatus = null; UpdateEnhancementUi();
        if (enhanced) await PrepareEnhancementAsync();
        else {
            changingBackendSettings = true; lastEnhancementStatus = null; UpdateEnhancementUi();
            try { await enhancement.StopOwnedAsync(); }
            finally { changingBackendSettings = false; lastEnhancementStatus = null; UpdateEnhancementUi(); UpdateOverviewModeHint(); }
        }
    }

    private async Task RunEnhancementActionAsync()
    {
        if (exiting || startingConnection || controller.Info.IsActive || enhancement.Status.Busy || preparingEnhancement || changingBackendSettings) return;
        switch (enhancement.Status.NextAction(settings.ManageHapticBackend)) {
            case EnhancementAction.UseManaged:
                await ChangeBackendSettingsAsync(true, settings.ViiperPort, checkAfterChange: true);
                break;
            case EnhancementAction.Connect:
                settings.OutputMode = GameOutputMode.DualSenseExperimental; enhancementFallback = null; SaveSettings();
                SelectPage("overview"); await BeginConnectionAsync();
                break;
            case EnhancementAction.Install:
                await PrepareEnhancementAsync(install: true);
                break;
            default:
                await PrepareEnhancementAsync(recheck: true);
                break;
        }
    }

    private void SyncBackendOptions()
    {
        updatingBackendOptions = true;
        try {
            if (externalBackend is not null) externalBackend.IsOn = !settings.ManageHapticBackend;
            if (backendPort is not null) backendPort.Value = settings.ViiperPort;
        } finally { updatingBackendOptions = false; }
    }

    private async Task ChangeBackendSettingsAsync(bool managed, int port, bool checkAfterChange)
    {
        if (exiting || startingConnection || controller.Info.IsActive || enhancement.Status.Busy || preparingEnhancement || changingBackendSettings) { SyncBackendOptions(); return; }
        if (settings.ManageHapticBackend == managed && settings.ViiperPort == port) return;
        changingBackendSettings = true;
        settings.ManageHapticBackend = managed; settings.ViiperPort = port;
        settings.OutputMode = GameOutputMode.Xbox; SaveSettings(); SyncBackendOptions();
        lastEnhancementStatus = null; UpdateEnhancementUi(); UpdateOverviewModeHint();
        try { await enhancement.StopOwnedAsync(); }
        finally { changingBackendSettings = false; lastEnhancementStatus = null; UpdateEnhancementUi(); }
        // Switching ownership or editing the port never grants permission to install.
        if (wantsEnhanced && checkAfterChange) await PrepareEnhancementAsync(recheck: true);
    }

    private async Task<bool> PrepareEnhancementAsync(bool install = false, bool recheck = false)
    {
        if (exiting || startingConnection || controller.Info.IsActive || enhancement.Status.Busy || preparingEnhancement || changingBackendSettings) return false;
        preparingEnhancement = true;
        lastEnhancementStatus = null; UpdateEnhancementUi();
        try {
            bool ready = await enhancement.PrepareAsync(settings, install, verifyAgain: recheck);
            if (ready && wantsEnhanced && !exiting && !startingConnection && !controller.Info.IsActive) {
                settings.OutputMode = GameOutputMode.DualSenseExperimental; enhancementFallback = null; SaveSettings();
            }
            else if (!ready && !startingConnection && !controller.Info.IsActive && settings.OutputMode != GameOutputMode.Xbox) {
                settings.OutputMode = GameOutputMode.Xbox; SaveSettings();
            }
            return ready && !exiting;
        } catch (OperationCanceledException) { return false; }
        finally { preparingEnhancement = false; lastEnhancementStatus = null; UpdateEnhancementUi(); UpdateOverviewModeHint(); }
    }

    private void OpenEnhancementSettings()
    {
        NativeMethods.ShowExisting(WinRT.Interop.WindowNative.GetWindowHandle(this)); Activate();
        SelectPage("settings");
        gameMode?.StartBringIntoView();
    }

    private void UpdateOverviewModeHint()
    {
        if (!cachedPages.TryGetValue("overview", out var overview)) return;
        bool enhanced = settings.OutputMode == GameOutputMode.DualSenseExperimental;
        overview.Live.gameModeTitle.Text = enhanced ? "原生触觉增强模式" : "通用兼容模式";
        overview.Live.gameModeHelp.Text = enhanced ? "通过虚拟 DualSense 适配游戏触觉。具体游戏的震感是否提升尚无法确认，建议实际体验后选择。" :
            enhancementFallback ?? "已包含普通游戏震动。支持原生触觉的游戏，可到设置中试用增强模式。";
    }

    private void UpdateEnhancementUi()
    {
        if (gameMode is null || enhancementPanel is null) return;
        var state = enhancement.Status;
        bool active = controller.Info.IsActive || startingConnection, native = controller.ReceivingNativeHaptics;
        if (ReferenceEquals(lastEnhancementStatus, state) && lastEnhancementActive == active && lastNativeFeedback == native) return;
        lastEnhancementStatus = state; lastEnhancementActive = active; lastNativeFeedback = native;
        bool busy = state.Busy || preparingEnhancement || changingBackendSettings;
        gameMode.IsEnabled = !active && !busy && !AppSettings.IsPreview;
        modeHint!.Text = active ? "请先断开 PC 连接，再切换游戏模式。" : "在启动游戏前选择。通用兼容适合大多数游戏，增强模式需要游戏支持。";
        enhancementPanel.Visibility = wantsEnhanced ? Visibility.Visible : Visibility.Collapsed;
        backendManagementHint!.Text = settings.ManageHapticBackend ? "由本应用自动管理：先检测已有组件，缺少时再提示安装。" : "连接其他软件的后端：需要先在原软件中启动，安装或重启不会自动启动它。";
        enhancementMessage!.Text = changingBackendSettings ? "正在切换后端设置…" : active && state.Ready && settings.OutputMode == GameOutputMode.DualSenseExperimental ?
            native ? "正在接收游戏原生触觉" : "增强模式已连接，等待游戏反馈" : state.Message;
        enhancementProgress!.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        enhancementProgress.IsIndeterminate = state.Phase != EnhancementPhase.Downloading;
        enhancementProgress.Value = state.Progress * 100;
        enhancementAction!.Content = changingBackendSettings ? "正在切换…" : state.ActionText(settings.ManageHapticBackend);
        enhancementAction.IsEnabled = !busy && !active && !AppSettings.IsPreview;
        enhancementCancel!.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        enhancementCancel.IsEnabled = state.Busy && state.Phase != EnhancementPhase.InstallingDriver;
        useXbox!.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        useXbox.IsEnabled = !active;
        bool needsInstall = settings.ManageHapticBackend && state.NextAction(true) == EnhancementAction.Install && !busy;
        installHint!.Visibility = needsInstall ? Visibility.Visible : Visibility.Collapsed;
        installHint.Text = state.Missing.HasFlag(MissingEnhancementComponents.Driver) ?
            "仅安装缺少的组件，已校验的下载会复用。驱动安装需确认管理员弹窗，可能短暂重连 USB 设备；完成后需重启电脑，应用不会自动重启。" :
            "仅下载并部署触觉组件，复用已安装驱动；无需重新安装驱动，也无需重启电脑。";
        enhancementGuide!.Text = changingBackendSettings ? "正在切换设置，请稍候。" :
            state.Phase == EnhancementPhase.Downloading ? "正在下载并校验缺少的组件，已安装的组件会保留。" :
            state.Phase == EnhancementPhase.InstallingDriver ? "请按 Windows 提示完成驱动安装；应用不会自动重启电脑。" :
            state.Busy ? "正在检测并验证虚拟手柄与触觉音频通道，请稍候。" :
            state.Ready ? "连接后再启动游戏；部分游戏需关闭该游戏的 Steam Input。声音输出保持原来的扬声器或耳机。" :
            !settings.ManageHapticBackend ? "可由原软件启动后点「重新检测」，或改用本应用自动管理并检测。切换管理方式不会安装组件。" :
            state.Phase == EnhancementPhase.RestartRequired ? "安装已完成，无需重复安装。保存工作并重启电脑，然后点「重新检测」。" :
            state.Phase is EnhancementPhase.Error or EnhancementPhase.Conflict ? "展开「查看问题详情」，处理后重新检测；也可使用通用模式继续游戏。" :
            needsInstall ? "安装完成后会继续验证虚拟手柄和触觉音频通道。" :
            "检测会复用已有组件，不会下载或重新安装。";
        enhancementDetail!.Text = state.Detail;
        enhancementIssue!.Visibility = !string.IsNullOrWhiteSpace(state.Detail) && !busy ? Visibility.Visible : Visibility.Collapsed;
        externalBackend!.IsEnabled = !busy && !active;
        backendPort!.IsEnabled = !busy && !active;
        recheckEnhancement!.IsEnabled = !busy && !active && !AppSettings.IsPreview;
        backendPort.Visibility = settings.ManageHapticBackend ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateRumbleStatus()
    {
        var status = controller.Haptics;
        foreach (var button in rumbleTests) button.IsEnabled = status.Ready && status.Enabled && settings.RumbleStrength > 0;
        if (rumbleStop is not null) rumbleStop.IsEnabled = status.Ready;
        if (rumbleStatus is not null) rumbleStatus.Text = status.Faulted ? status.Message :
            !settings.RumbleEnabled || settings.RumbleStrength == 0 ? "震动已关闭，可在设置中开启或调整强度" :
            !status.Ready ? status.Message : status.Testing ? "正在测试 · 自动停止，也可随时点「立即停止」" :
            settings.OutputMode == GameOutputMode.DualSenseExperimental ? controller.ReceivingNativeHaptics ? "正在接收游戏原生触觉" : "增强模式已连接 · 可测试震动，或启动支持原生触觉的游戏" :
            "震动已就绪 · 可选择效果，或在游戏中体验";
        if (rumbleTelemetry is not null) {
            rumbleTelemetry.Text = status.Ready ? $"游戏反馈 {status.Feedback:N0} 次 · 低频 {status.Low / 255d:P0} / 高频 {status.High / 255d:P0}" : "";
            rumbleTelemetry.Visibility = status.Ready ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
