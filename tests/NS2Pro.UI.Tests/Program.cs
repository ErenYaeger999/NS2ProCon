using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Report = Path.Combine(AppContext.BaseDirectory, "dependency-smoke-result.json");
    private static readonly List<string> Passed = [];
    [STAThread]
    private static void Main()
    {
        try {
            if (!Environment.GetCommandLineArgs().Contains("--preview-onboarding")) throw new Exception("Preview isolation is required");
            var assembly = typeof(NS2Pro.App.MainWindow).Assembly;
            var instanceType = assembly.GetType("NS2Pro.App.Services.SingleInstance")!;
            using var instance = (IDisposable)Activator.CreateInstance(instanceType, ["Local\\NS2Pro.DependencySmoke." + Guid.NewGuid()])!;
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ => {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                var appType = assembly.GetType("NS2Pro.App.App")!;
                var app = (Application)Activator.CreateInstance(appType, Private | BindingFlags.Public, null, [instance], null)!;
                app.UnhandledException += (_, args) => { WriteFailure(args.Exception); args.Handled = true; app.Exit(); };
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += async (_, _) => {
                    timer.Stop();
                    NS2Pro.App.MainWindow? window = null;
                    try {
                        window = (NS2Pro.App.MainWindow)appType.GetField("window", Private)!.GetValue(app)!;
                        if (window is null) throw new Exception("The real app did not create its window");
                        Passed.Add("Actual application XAML resources and native window initialized");
                        var navigation = (Microsoft.UI.Xaml.Controls.NavigationView)window.GetType().GetField("navigation", Private)!.GetValue(window)!;
                        if (navigation.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().Any(x => Equals(x.Tag, "setup"))) throw new Exception("Guide still occupies primary navigation");
                        var footer = navigation.FooterMenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().ToArray();
                        if (footer.Length != 2 || !Equals(footer[0].Tag, "setup") || !footer[0].SelectsOnInvoked || footer[1].SelectsOnInvoked)
                            throw new Exception("Guide/contact footer navigation is incorrect");
                        Passed.Add("Guide is selectable in the footer above the contact action");
                        foreach (var page in new[] { "overview", "setup", "test", "mapping", "settings" }) {
                            window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, [page]);
                            window.Content.UpdateLayout();
                            if (!Equals(window.GetType().GetField("selectedPage", Private)!.GetValue(window), page)) throw new Exception("Page selection did not navigate: " + page);
                            if ((bool)window.GetType().GetField("pageFailed", Private)!.GetValue(window)!) throw new Exception("Page construction failed: " + page);
                            Passed.Add("Actual page and vector controls constructed: " + page);
                        }
                        var pages = (System.Collections.IDictionary)window.GetType().GetField("cachedPages", Private)!.GetValue(window)!;
                        object PageLive(string key) => pages[key]!.GetType().GetProperty("Live")!.GetValue(pages[key])!;
                        Dictionary<NS2Pro.Core.ButtonLayout, Microsoft.UI.Xaml.Controls.RadioButton> Options(string key) {
                            var controls = PageLive(key);
                            return (Dictionary<NS2Pro.Core.ButtonLayout, Microsoft.UI.Xaml.Controls.RadioButton>)controls.GetType().GetField("layoutOptions")!.GetValue(controls)!;
                        }
                        var overviewOptions = Options("overview"); var mappingOptions = Options("mapping");
                        var layoutSettings = (NS2Pro.App.Services.AppSettings)window.GetType().GetField("settings", Private)!.GetValue(window)!;
                        var initialLayout = layoutSettings.Layout;
                        foreach (var selectedLayout in new[] { NS2Pro.Core.ButtonLayout.Positions, NS2Pro.Core.ButtonLayout.Labels }) {
                            overviewOptions[selectedLayout].IsChecked = true;
                            if (layoutSettings.Layout != selectedLayout || mappingOptions[selectedLayout].IsChecked != true ||
                                mappingOptions.Values.Count(x => x.IsChecked == true) != 1 || overviewOptions.Values.Count(x => x.IsChecked == true) != 1)
                                throw new Exception("Overview layout selection is not synchronized with mapping");
                        }
                        mappingOptions[NS2Pro.Core.ButtonLayout.Positions].IsChecked = true;
                        if (layoutSettings.Layout != NS2Pro.Core.ButtonLayout.Positions || overviewOptions[NS2Pro.Core.ButtonLayout.Positions].IsChecked != true)
                            throw new Exception("Mapping selection is not synchronized back to overview");
                        overviewOptions[initialLayout].IsChecked = true;
                        Passed.Add("Both overview presets update settings immediately; selection synchronizes in both directions with mapping");
                        window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, ["overview"]);
                        window.Content.UpdateLayout();
                        var optionGroup = (Microsoft.UI.Xaml.Controls.StackPanel)overviewOptions.Values.First().Parent;
                        var optionBorder = (Microsoft.UI.Xaml.Controls.Border)optionGroup.Parent;
                        var mappingRow = (Microsoft.UI.Xaml.Controls.Grid)optionBorder.Parent;
                        var rowCard = (Microsoft.UI.Xaml.Controls.Border)mappingRow.Parent;
                        if (rowCard.ActualHeight > 85 || Microsoft.UI.Xaml.Controls.Grid.GetRow(optionBorder) != 0 || mappingRow.Children.Count != 2)
                            throw new Exception($"Overview preset is not a compact single row: {rowCard.ActualWidth} x {rowCard.ActualHeight}");
                        Passed.Add($"Overview presets share a compact row ({rowCard.ActualWidth:F0} x {rowCard.ActualHeight:F0}), without a separate edit button");
                        mappingRow.Width = 310; window.Content.UpdateLayout(); await Task.Delay(100); window.Content.UpdateLayout();
                        if (Microsoft.UI.Xaml.Controls.Grid.GetRow(optionBorder) != 1 || optionBorder.ActualWidth > 310)
                            throw new Exception("Narrow layout clips the preset selector");
                        mappingRow.Width = double.NaN; window.Content.UpdateLayout(); await Task.Delay(100); window.Content.UpdateLayout();
                        if (Microsoft.UI.Xaml.Controls.Grid.GetRow(optionBorder) != 0) throw new Exception("Wide layout did not return to one row");
                        Passed.Add("Narrow layout wraps without clipping and returns to one row when widened");
                        window.GetType().GetField("wantsEnhanced", Private)!.SetValue(window, false);
                        window.GetType().GetField("lastEnhancementStatus", Private)!.SetValue(window, null);
                        window.GetType().GetMethod("UpdateEnhancementUi", Private)!.Invoke(window, null);
                        var modes = (Microsoft.UI.Xaml.Controls.ComboBox)window.GetType().GetField("gameMode", Private)!.GetValue(window)!;
                        var panel = (Microsoft.UI.Xaml.Controls.StackPanel)window.GetType().GetField("enhancementPanel", Private)!.GetValue(window)!;
                        if (modes.Items.Count != 2 || panel.Visibility != Visibility.Collapsed) throw new Exception("Default game mode must stay compact");
                        Passed.Add("Default game mode stays compact with enhanced details hidden");
                        window.GetType().GetField("wantsEnhanced", Private)!.SetValue(window, true);
                        var manager = window.GetType().GetField("enhancement", Private)!.GetValue(window)!;
                        var setState = manager.GetType().GetMethod("Set", Private)!;
                        var refresh = window.GetType().GetMethod("UpdateEnhancementUi", Private)!;
                        var primary = (Microsoft.UI.Xaml.Controls.Button)window.GetType().GetField("enhancementAction", Private)!.GetValue(window)!;
                        var cancelButton = (Microsoft.UI.Xaml.Controls.Button)window.GetType().GetField("enhancementCancel", Private)!.GetValue(window)!;
                        var previewController = (NS2Pro.App.Services.ControllerService)window.GetType().GetField("controller", Private)!.GetValue(window)!;
                        var previewSettings = (NS2Pro.App.Services.AppSettings)window.GetType().GetField("settings", Private)!.GetValue(window)!;
                        window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, ["overview"]);
                        window.Content.UpdateLayout();
                        var overviewText = string.Join("\n", Descendants((DependencyObject)window.Content).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Select(x => x.Text));
                        if (!overviewText.Contains("回报率") || !overviewText.Contains("本机转发耗时") ||
                            !Descendants((DependencyObject)window.Content).OfType<Microsoft.UI.Xaml.Controls.HyperlinkButton>().Any(x => Equals(x.Content, "更换手柄")))
                            throw new Exception("Overview lacks cadence, honest host timing or change-controller action");
                        Passed.Add("Overview offers real report cadence, host-only forwarding time and an explicit change-controller action");
                        string diagnostics = await NS2Pro.App.Services.DiagnosticService.CollectAsync(previewSettings, previewController,
                            (NS2Pro.App.Services.EnhancementService)manager, ["设备 AA:BB:CC:DD:EE:FF · " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]);
                        if (!diagnostics.Contains("回报率") || !diagnostics.Contains("本机转发") ||
                            !diagnostics.Contains("蓝牙适配器：") || diagnostics.Contains("Property key syntax error") ||
                            diagnostics.Contains("AA:BB:CC:DD:EE:FF") || diagnostics.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                            throw new Exception("Diagnostics omit timing or expose the user directory / full controller address");
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics-check.txt"), diagnostics);
                        Passed.Add("Real Windows diagnostics collect system / mode / timing data and redact private paths and controller addresses");
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.NotInstalled, "首次使用需安装增强组件和官方驱动。", 0d, ""]);
                        refresh.Invoke(window, null);
                        modes.SelectedIndex = 1;
                        window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, ["settings"]);
                        window.Content.UpdateLayout();
                        var usage = panel.Children.OfType<Microsoft.UI.Xaml.Controls.Border>().Single();
                        var copy = string.Join("\n", Descendants(usage).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Select(x => x.Text));
                        if (panel.Visibility != Visibility.Visible || !copy.Contains("Xbox／ABXY") || !copy.Contains("暂无法统一替换") || copy.Contains("声音") || copy.Contains("麦克风"))
                            throw new Exception("Enhanced mode must retain PS-icon guidance without obsolete audio reminders");
                        if (Descendants(usage).OfType<Microsoft.UI.Xaml.Controls.HyperlinkButton>().Any())
                            throw new Exception("Enhanced guidance retains an obsolete sound settings link");
                        Passed.Add("Enhanced selection retains PS-icon limitations without obsolete audio reminders or sound settings links");
                        if (Environment.GetCommandLineArgs().Contains("--capture-enhancement")) await Capture(usage, "enhancement-guidance-wide.png");
                        panel.Width = 340; window.Content.UpdateLayout(); await Task.Delay(100); window.Content.UpdateLayout();
                        if (usage.ActualWidth > 340 || Descendants(usage).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(x => x.ActualWidth > usage.ActualWidth))
                            throw new Exception("Enhanced guidance clips at narrow widths");
                        if (Environment.GetCommandLineArgs().Contains("--capture-enhancement")) await Capture(usage, "enhancement-guidance-narrow.png");
                        panel.Width = double.NaN; window.Content.UpdateLayout();
                        window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, ["overview"]);
                        var overviewLive = PageLive("overview");
                        var overviewNotice = (Microsoft.UI.Xaml.Controls.Border)overviewLive.GetType().GetField("enhancedUsageNotice")!.GetValue(overviewLive)!;
                        var refreshOverview = window.GetType().GetMethod("UpdateOverviewModeHint", Private)!;
                        previewSettings.OutputMode = NS2Pro.App.Services.GameOutputMode.DualSenseExperimental;
                        refreshOverview.Invoke(window, null); window.Content.UpdateLayout(); await Task.Delay(100);
                        var overviewCopy = string.Join("\n", Descendants(overviewNotice).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Select(x => x.Text));
                        if (overviewNotice.Visibility != Visibility.Visible || !overviewCopy.Contains("PS 图标") || overviewCopy.Contains("声音") || overviewCopy.Contains("麦克风"))
                            throw new Exception("Active enhanced output lacks its overview reminder");
                        if (Environment.GetCommandLineArgs().Contains("--capture-enhancement")) await Capture((FrameworkElement)window.Content, "enhancement-overview.png");
                        previewSettings.OutputMode = NS2Pro.App.Services.GameOutputMode.Xbox; previewSettings.PreferNativeHaptics = false;
                        refreshOverview.Invoke(window, null);
                        window.GetType().GetField("wantsEnhanced", Private)!.SetValue(window, false);
                        window.GetType().GetField("lastEnhancementStatus", Private)!.SetValue(window, null); refresh.Invoke(window, null);
                        if (overviewNotice.Visibility != Visibility.Collapsed || panel.Visibility != Visibility.Collapsed)
                            throw new Exception("Common output retains irrelevant enhanced-mode warnings");
                        Passed.Add("Compact guidance follows the actual output mode, common mode hides it, and narrow text wraps without clipping");
                        previewSettings.PreferNativeHaptics = true;
                        previewController.GetType().GetField("activeOutputMode", Private)!.SetValue(previewController, NS2Pro.App.Services.GameOutputMode.Xbox);
                        previewController.GetType().GetField("info", Private)!.SetValue(previewController,
                            new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.Searching, "本次使用通用兼容"));
                        refreshOverview.Invoke(window, null);
                        if (overviewNotice.Visibility != Visibility.Collapsed || !previewSettings.PreferNativeHaptics)
                            throw new Exception("Fallback either shows the wrong actual output or overwrites preference");
                        previewController.GetType().GetField("info", Private)!.SetValue(previewController, new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.Idle, ""));
                        var guide = window.GetType().GetField("connectionGuide", Private)!.GetValue(window)!;
                        guide.GetType().GetMethod("Update")!.Invoke(guide, [previewController.Info, null]);
                        if (!((Microsoft.UI.Xaml.Controls.TextBlock)guide.GetType().GetField("outputHelp", Private)!.GetValue(guide)!).Text.Contains("DualSense"))
                            throw new Exception("Guide still labels enhanced output as Xbox");
                        previewSettings.PreferNativeHaptics = false;
                        Passed.Add("Fallback displays the effective output while keeping preference; the guide describes the selected DualSense mode accurately");
                        window.GetType().GetField("wantsEnhanced", Private)!.SetValue(window, true);
                        window.GetType().GetField("lastEnhancementStatus", Private)!.SetValue(window, null); refresh.Invoke(window, null);
                        previewSettings.ManageHapticBackend = true;
                        foreach (var phase in Enum.GetValues<NS2Pro.App.Services.EnhancementPhase>()) {
                            setState.Invoke(manager, [phase, "界面状态检查", .45, "检查详情"]); refresh.Invoke(window, null); window.Content.UpdateLayout();
                            var progress = (Microsoft.UI.Xaml.Controls.ProgressBar)window.GetType().GetField("enhancementProgress", Private)!.GetValue(window)!;
                            var status = (NS2Pro.App.Services.EnhancementStatus)manager.GetType().GetProperty("Status")!.GetValue(manager)!;
                            if ((progress.Visibility == Visibility.Visible) != status.Busy) throw new Exception("Incorrect progress state " + phase);
                            if (phase == NS2Pro.App.Services.EnhancementPhase.InstallingDriver && cancelButton.IsEnabled) throw new Exception("Driver installer can be forcibly interrupted");
                            if (phase is NS2Pro.App.Services.EnhancementPhase.RestartRequired or NS2Pro.App.Services.EnhancementPhase.Conflict && !Equals(primary.Content, "重新检测")) throw new Exception("Incorrect recovery action " + phase);
                        }
                        Passed.Add("All enhancement states construct; progress, restart/conflict recovery and installer cancellation agree");
                        var installHelp = (Microsoft.UI.Xaml.Controls.TextBlock)window.GetType().GetField("installHint", Private)!.GetValue(window)!;
                        var problem = (Microsoft.UI.Xaml.Controls.Expander)window.GetType().GetField("enhancementIssue", Private)!.GetValue(window)!;
                        var statusField = manager.GetType().GetField("status", Private)!;
                        statusField.SetValue(manager, NS2Pro.App.Services.EnhancementStatus.PrerequisiteIssue(NS2Pro.App.Services.EnhancementComponents.DriverVersion, true, false));
                        refresh.Invoke(window, null);
                        if (!Equals(primary.Content, "安装触觉组件 · 约 4 MB") || installHelp.Visibility != Visibility.Visible || !installHelp.Text.Contains("无需重启"))
                            throw new Exception("Backend-only installation unnecessarily asks for driver setup or reboot");
                        statusField.SetValue(manager, NS2Pro.App.Services.EnhancementStatus.PrerequisiteIssue(NS2Pro.App.Services.EnhancementComponents.DriverVersion, false, false, "CLI failure"));
                        refresh.Invoke(window, null);
                        if (!Equals(primary.Content, "重新检测") || installHelp.Visibility != Visibility.Collapsed || problem.Visibility != Visibility.Visible)
                            throw new Exception("Driver diagnostic error is presented as an installation");
                        previewSettings.ManageHapticBackend = false;
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.Error, "外部后端未启动", 0d, "未找到本机服务"]);
                        refresh.Invoke(window, null);
                        await ((Task)window.GetType().GetMethod("RunEnhancementActionAsync", Private)!.Invoke(window, null)!).WaitAsync(TimeSpan.FromSeconds(5));
                        var automaticToggle = (Microsoft.UI.Xaml.Controls.ToggleSwitch)window.GetType().GetField("automaticBackend", Private)!.GetValue(window)!;
                        if (!previewSettings.ManageHapticBackend || !automaticToggle.IsOn || ((NS2Pro.App.Services.EnhancementService)manager).Status.Phase != NS2Pro.App.Services.EnhancementPhase.Unchecked || installHelp.Visibility != Visibility.Collapsed)
                            throw new Exception("Switch-to-managed action retains external state or implies installation");
                        if (!Equals(automaticToggle.Header, "自动管理增强后端（推荐）") || !Equals(automaticToggle.OnContent, "由本应用启动和管理"))
                            throw new Exception("Automatic management toggle does not describe the enabled state");
                        var manualPort = (Microsoft.UI.Xaml.Controls.NumberBox)window.GetType().GetField("backendPort", Private)!.GetValue(window)!;
                        automaticToggle.IsOn = false; await Task.Delay(50);
                        if (previewSettings.ManageHapticBackend || NS2Pro.App.Services.AppSettings.Load().ManageHapticBackend || manualPort.Visibility != Visibility.Visible)
                            throw new Exception("Switching off fails to persist manual ownership or reveal its port");
                        automaticToggle.IsOn = true; await Task.Delay(50);
                        if (!previewSettings.ManageHapticBackend || !NS2Pro.App.Services.AppSettings.Load().ManageHapticBackend || manualPort.Visibility != Visibility.Collapsed)
                            throw new Exception("Switching on fails to persist automatic ownership or hide the manual port");
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.Checking, "正在检测", 0d, ""]); refresh.Invoke(window, null);
                        automaticToggle.IsOn = false;
                        if (automaticToggle.IsEnabled || !automaticToggle.IsOn || !previewSettings.ManageHapticBackend)
                            throw new Exception("Busy state accepts an ownership change or leaves the switch visually reversed");
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.Unchecked, "尚未检测", 0d, ""]);
                        previewController.GetType().GetField("info", Private)!.SetValue(previewController, new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.Preparing, "正在准备连接")); refresh.Invoke(window, null);
                        automaticToggle.IsOn = false;
                        if (automaticToggle.IsEnabled || !automaticToggle.IsOn || !previewSettings.ManageHapticBackend)
                            throw new Exception("Starting connection allows the management toggle to contradict settings");
                        previewController.GetType().GetField("info", Private)!.SetValue(previewController, new NS2Pro.App.Services.ConnectionInfo(NS2Pro.App.Services.ConnectionPhase.Idle, ""));
                        window.GetType().GetField("lastEnhancementStatus", Private)!.SetValue(window, null); refresh.Invoke(window, null);
                        Passed.Add("Automatic toggle on/off agrees with ownership, saved settings and manual port; busy/connecting changes revert consistently");
                        previewSettings.OutputMode = NS2Pro.App.Services.GameOutputMode.DualSenseExperimental;
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.Ready, "已就绪", 0d, ""]);
                        await ((Task)window.GetType().GetMethod("ChangeBackendSettingsAsync", Private)!.Invoke(window, [false, 4567, false])!).WaitAsync(TimeSpan.FromSeconds(5));
                        if (previewSettings.OutputMode != NS2Pro.App.Services.GameOutputMode.Xbox || ((NS2Pro.App.Services.EnhancementService)manager).Status.Ready || !Equals(primary.Content, "检测已有后端"))
                            throw new Exception("Changing backend ownership/port retains stale readiness");
                        await ((Task)window.GetType().GetMethod("ChangeBackendSettingsAsync", Private)!.Invoke(window, [true, 3242, false])!).WaitAsync(TimeSpan.FromSeconds(5));
                        Passed.Add("Managed action, backend-only install, driver failure details and changed-port readiness follow distinct paths");
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.NotInstalled, "首次使用需安装增强组件和官方驱动。", 0d, ""]);
                        refresh.Invoke(window, null);
                        if (Environment.GetCommandLineArgs().Contains("--inspect-enhancement")) {
                            modes.SelectedIndex = 1;
                            window.GetType().GetMethod("OpenEnhancementSettings", Private)!.Invoke(window, null);
                            await Task.Delay(TimeSpan.FromSeconds(90));
                        }
                        var service = window.GetType().GetField("controller", Private)!.GetValue(window)!;
                        var info = service.GetType().GetProperty("Info")!.GetValue(service)!;
                        if (info.GetType().GetProperty("Phase")!.GetValue(info)!.ToString() != "Idle") throw new Exception("Unexpected hardware connection attempt");
                        Passed.Add("No hardware connection was started");
                        var textSource = new Microsoft.UI.Xaml.Controls.TextBlock { Text = "长按顶部 SYNC，直到指示灯闪烁。", FontSize = 14 };
                        var inlineHint = NS2Pro.App.Controls.SyncHint.ForText(textSource);
                        var paragraph = (Microsoft.UI.Xaml.Documents.Paragraph)inlineHint.Blocks[0];
                        if (paragraph.Inlines.Count != 3 || paragraph.Inlines[0] is not Microsoft.UI.Xaml.Documents.Run prefix ||
                            !prefix.Text.EndsWith("SYNC\u2060") ||
                            paragraph.Inlines[1] is not Microsoft.UI.Xaml.Documents.InlineUIContainer container ||
                            container.Child is not Microsoft.UI.Xaml.Controls.Button hintButton ||
                            !Equals(Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(hintButton), NS2Pro.App.Controls.SyncHint.Location))
                            throw new Exception("SYNC is not in the surrounding text flow with an adjacent hint");
                        textSource.Text = "已连接";
                        if (paragraph.Inlines.Count != 1 || paragraph.Inlines[0] is not Microsoft.UI.Xaml.Documents.Run) throw new Exception("Stale inline hint after state changed");
                        Passed.Add("SYNC shares the surrounding text baseline, with a joined hint; ordinary status has no stale hint");
                        var appSettings = (NS2Pro.App.Services.AppSettings)window.GetType().GetField("settings", Private)!.GetValue(window)!;
                        appSettings.OutputMode = NS2Pro.App.Services.GameOutputMode.Xbox;
                        appSettings.PreferNativeHaptics = true;
                        window.GetType().GetField("wantsEnhanced", Private)!.SetValue(window, true);
                        setState.Invoke(manager, [NS2Pro.App.Services.EnhancementPhase.Error, "增强不可用", 0d, ""]);
                        window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, ["overview"]);
                        await ((Task)window.GetType().GetMethod("BeginConnectionAsync", Private)!.Invoke(window, null)!).WaitAsync(TimeSpan.FromSeconds(15));
                        if (manager.GetType().GetProperty("Status")!.GetValue(manager) is not NS2Pro.App.Services.EnhancementStatus attempted ||
                            attempted.Phase != NS2Pro.App.Services.EnhancementPhase.Error ||
                            !Equals(window.GetType().GetField("selectedPage", Private)!.GetValue(window), "overview"))
                            throw new Exception("Pending enhancement setup interrupted the default connection");
                        if (appSettings.OutputMode != NS2Pro.App.Services.GameOutputMode.Xbox) throw new Exception("Default output silently changed");
                        Passed.Add("Unavailable optional enhancement never redirects or blocks the default Xbox connection (hardware isolated by preview)");
                        var showUpdate = window.GetType().GetMethod("ShowUpdateResult", Private)!;
                        showUpdate.Invoke(window, [new NS2Pro.App.Services.UpdateCheckResult(NS2Pro.App.Services.UpdateCheckState.Available, "0.8.0")]);
                        window.Content.UpdateLayout();
                        var notice = (Microsoft.UI.Xaml.Controls.InfoBar)window.GetType().GetField("updateNotice", Private)!.GetValue(window)!;
                        if (!notice.IsOpen || notice.Visibility != Visibility.Visible || notice.Title != "发现新版本 v0.8.0") throw new Exception("Update banner did not open");
                        var link = (Microsoft.UI.Xaml.Controls.HyperlinkButton)notice.ActionButton;
                        if (link.NavigateUri.AbsoluteUri != NS2Pro.App.Services.UpdateService.ReleasesUrl) throw new Exception("Unexpected update destination");
                        Passed.Add("New-version notice opens with the fixed Release download link");
                        notice.IsOpen = false;
                        if (notice.Visibility != Visibility.Collapsed) throw new Exception("Dismissed update notice reserves layout space");
                        showUpdate.Invoke(window, [new NS2Pro.App.Services.UpdateCheckResult(NS2Pro.App.Services.UpdateCheckState.Available, "0.8.0")]);
                        showUpdate.Invoke(window, [new NS2Pro.App.Services.UpdateCheckResult(NS2Pro.App.Services.UpdateCheckState.Current, "0.7.0")]);
                        if (notice.IsOpen || notice.Visibility != Visibility.Collapsed) throw new Exception("Withdrawn update leaves a stale banner");
                        Passed.Add("Update notice dismisses and clears if the current version needs no update");
                        var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().ToArray();
                        foreach (var name in new[] { "Microsoft.ui.xaml.dll", "Microsoft.WindowsAppRuntime.dll", "Microsoft.UI.Windowing.Core.dll" }) {
                            var module = modules.FirstOrDefault(m => string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase));
                            if (module is null || !Path.GetDirectoryName(module.FileName)!.Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                                throw new Exception("Expected app-local runtime module: " + name);
                            Passed.Add("Loaded app-local runtime: " + name);
                        }
                        File.WriteAllText(Report, JsonSerializer.Serialize(new { Passed }, new JsonSerializerOptions { WriteIndented = true }));
                        if (Environment.GetCommandLineArgs().Contains("--inspect-overview")) {
                            window.GetType().GetMethod("SelectPage", Private)!.Invoke(window, ["overview"]);
                            await Task.Delay(TimeSpan.FromSeconds(90));
                        }

                    } catch (Exception error) { WriteFailure(error); }
                    finally {
                        if (window is not null) await (Task)window.GetType().GetMethod("ExitAsync", Private)!.Invoke(window, null)!;
                        else app.Exit();
                    }
                };
                timer.Start();
            });
        } catch (Exception error) { WriteFailure(error); }
    }
    private static void WriteFailure(Exception error) { File.WriteAllText(Report, JsonSerializer.Serialize(new { Passed, Error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true })); Environment.ExitCode = 1; }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        yield return item;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(item, i))) yield return child;
    }
    private static async Task Capture(FrameworkElement element, string name)
    {
        await Task.Delay(100);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var buffer = await bitmap.GetPixelsAsync();
        var bytes = new byte[buffer.Length];
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(AppContext.BaseDirectory);
        var file = await folder.CreateFileAsync(name, Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}
