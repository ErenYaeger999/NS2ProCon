using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NS2Pro.App.Controls;
using NS2Pro.App.Services;
using NS2Pro.Core;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace NS2Pro.App;

public sealed partial class MainWindow : Window
{
    private static readonly string AppVersion = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private readonly AppSettings settings = AppSettings.Load();
    private readonly EnhancementService enhancement = new(HapticEnvironmentProbe.VerifyAsync);
    private readonly ControllerService controller;
    private readonly NavigationView navigation = new();
    private StackPanel page = new() { Spacing = 22 };
    // WinUI overlays scrollbars on content; reserve a gutter beyond the cards.
    private readonly ScrollViewer pageScroll = new() {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Padding = new(0, 0, 24, 0)
    };
    private readonly TextBlock title = Text("连接总览", 28, true);
    private readonly TextBlock subtitle = Text("连接 PC，随时回到主机。", 13, false, AppPalette.Muted);
    private LiveControls live = new();
    private readonly Grid pageHost = new();
    private readonly Dictionary<string, CachedPage> cachedPages = new();
    private StackPanel? pageError;
    private bool pageFailed;
    private TextBlock badge => live.badge;
    private TextBlock statusText => live.statusText;
    private TextBlock addressText => live.addressText;
    private TextBlock hzText => live.hzText;
    private TextBlock batteryText => live.batteryText;
    private TextBlock inputText => live.inputText;
    private ControllerFaceView controllerView => live.controllerView;
    private StickPlot leftPlot => live.leftPlot;
    private StickPlot rightPlot => live.rightPlot;
    private TextBlock leftValues => live.leftValues;
    private TextBlock rightValues => live.rightValues;
    private TextBlock leftOutput => live.leftOutput;
    private TextBlock rightOutput => live.rightOutput;
    private TextBlock leftOffset => live.leftOffset;
    private TextBlock rightOffset => live.rightOffset;
    private ProgressBar leftTrigger => live.leftTrigger;
    private ProgressBar rightTrigger => live.rightTrigger;
    private TextBlock xboxButtons => live.xboxButtons;
    private TextBox logBox => live.logBox;
    private Button connectButton => live.connectButton;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private TrayIcon? tray;
    private PowerNotifications? powerNotifications;
    private bool exiting, exitAllowed;
    private string selectedPage = "overview";
    private readonly Queue<string> logLines = new();
    private TextBox? addressEditor;
    private bool addressDirty, updatingAddress;
    private bool hadLiveInput;
    private bool startingConnection;
    private string? enhancementFallback;

    public MainWindow()
    {
        wantsEnhanced = settings.PreferNativeHaptics || settings.OutputMode == GameOutputMode.DualSenseExperimental;
        controller = new(settings, enhancement);
        Title = App.WindowTitle;
        ExtendsContentIntoTitleBar = true;
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try { powerNotifications = new(hwnd, controller.SetSystemSuspended); }
        catch (Exception error) { AppLog.Write("warn", error.Message); }
        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(hwnd) / 96.0);
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min((int)(1200 * scale), workArea.Width - (int)(40 * scale)), Math.Min((int)(870 * scale), workArea.Height - (int)(40 * scale))));
        AppWindow.Move(new PointInt32(workArea.X + (workArea.Width - AppWindow.Size.Width) / 2, workArea.Y + (workArea.Height - AppWindow.Size.Height) / 2));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        var root = new Grid { RequestedTheme = ElementTheme.Light, Background = Brush(AppPalette.Page) };
        root.RowDefinitions.Add(new() { Height = new(44) });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var titlebar = new Grid { Margin = new(20, 0, 150, 0) };
        var appName = Text(AppIdentity.DisplayName, 12, true, "565656");
        appName.VerticalAlignment = VerticalAlignment.Center;
        titlebar.Children.Add(appName); root.Children.Add(titlebar); SetTitleBar(titlebar);
        navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        navigation.OpenPaneLength = 205;
        navigation.IsPaneOpen = true;
        navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        navigation.IsPaneToggleButtonVisible = false;
        navigation.IsSettingsVisible = false;
        navigation.IsTitleBarAutoPaddingEnabled = false;
        navigation.PaneHeader = Brand();
        ConfigureSidebarFooter();
        AddNavigation("连接总览", "overview", "\uE701");
        AddNavigation("手柄测试", "test", "\uE7FC");
        AddNavigation("按键映射", "mapping", "\uE8AB");
        AddNavigation("设置与诊断", "settings", "\uE713");
        navigation.SelectionChanged += (_, args) => {
            if (args.SelectedItem is NavigationViewItem item) {
                selectedPage = (string)item.Tag;
                TryBuildPage();
            }
        };
        var scroll = pageScroll;
        scroll.Content = pageHost;
        var content = new Grid { Margin = new(26, 13, 4, 24) };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 5, Margin = new(0, 0, 0, 24), Children = { title, subtitle } };
        if (settings.ShouldShowConnectionNotice) {
            var notice = new InfoBar { IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Informational,
                Title = "连接小提示",
                Message = "连接成功后，手柄指示灯持续闪烁属于正常现象。回到 NS2 时，先断开 PC 连接，再按 HOME。",
                Margin = new(0, 12, 24, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            bool dismissedThisRun = false;
            notice.Closed += (_, args) => {
                notice.Visibility = Visibility.Collapsed;
                if (args.Reason != InfoBarCloseReason.CloseButton || dismissedThisRun) return;
                dismissedThisRun = true;
                settings.DismissConnectionNotice(); SaveSettings();
            };
            heading.Children.Add(notice);
        }
        heading.Children.Add(CreateUpdateNotice());
        content.Children.Add(heading); Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        navigation.Content = content; Grid.SetRow(navigation, 1); root.Children.Add(navigation); Content = root;
        controller.Changed += info => DispatcherQueue.TryEnqueue(() => UpdateStatus(info));
        AppLog.LineWritten += LogWritten;
        if (File.Exists(AppLog.FilePath)) foreach (var line in File.ReadLines(AppLog.FilePath).TakeLast(30)) logLines.Enqueue(line);
        timer.Tick += (_, _) => UpdateLiveInput(); timer.Start();
        AppWindow.Closing += async (_, args) => {
            if (exitAllowed) return;
            args.Cancel = true;
            if (settings.CloseToTray && tray is not null) NativeMethods.ShowWindow(hwnd, 0);
            else await ExitAsync();
        };
        Closed += (_, _) => { updateCancellation.Cancel(); timer.Stop(); foreach (var cached in cachedPages.Values) cached.Live.Model?.Dispose(); powerNotifications?.Dispose(); tray?.Dispose(); AppLog.LineWritten -= LogWritten; };
        try { tray = new(hwnd, () => DispatcherQueue.TryEnqueue(() => { NativeMethods.ShowExisting(hwnd); Activate(); }),
            () => DispatcherQueue.TryEnqueue(async () => await ExitAsync()),
            () => DispatcherQueue.TryEnqueue(async () => await SetTrayConnectionAsync(true)),
            () => DispatcherQueue.TryEnqueue(async () => await SetTrayConnectionAsync(false))); }
        catch (Exception error) { AppLog.Write("warn", error.Message); }
        bool initialized = false;
        root.Loaded += (_, _) => {
            if (initialized) return;
            initialized = true;
            bool showGuide = AppSettings.IsPreview || settings.ShouldShowConnectionGuide;
            settings.HasSeenConnectionGuide = true; SaveSettings();
            SelectPage(showGuide ? "setup" : "overview");
            AppLog.Write("info", $"{AppIdentity.DisplayName} v{AppVersion} 已启动 · WinUI 3");
            AppLog.Write("info", "运行位置：" + AppContext.BaseDirectory);
            if (settings.AutoStartConnection && !AppSettings.IsPreview) _ = SetTrayConnectionAsync(true);
            _ = CheckUpdatesAsync();
        };
    }

    private void AddNavigation(string label, string tag, string glyph) => navigation.MenuItems.Add(new NavigationViewItem {
        Content = label, Tag = tag, Icon = new FontIcon { Glyph = glyph, FontFamily = new("Segoe Fluent Icons") }, Margin = new(8, 3, 8, 3) });
    private static StackPanel Brand() => new() { Margin = new(20, 16, 0, 24), Spacing = 6,
        Children = { new Image { Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Left,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "App.png"))) },
            Text(AppIdentity.DisplayName, 18, true), Text("一只手柄，自在切换", 11, false, AppPalette.Muted) } };

    // Each page owns its visual tree for its entire lifetime. Never reparent focused buttons,
    // status elements, or a live preview while WinUI is processing navigation/focus events.
    private sealed class LiveControls
    {
        public readonly TextBlock badge = Text("未连接", 12, true, "666666");
        public readonly TextBlock statusText = Text("", 14);
        public readonly TextBlock connectionHint = Text("先点击连接，再长按手柄顶部的 SYNC 小键。", 13, false, AppPalette.Muted);
        public readonly TextBlock addressText = Text("蓝牙 LE · 保留 NS2 配对", 12, false, AppPalette.Muted);
        public readonly TextBlock hzText = Text("—", 26, true);
        public readonly TextBlock batteryText = Text("—", 26, true);
        public readonly BatteryIcon batteryIcon = new();
        public readonly TextBlock batteryState = new() { FontSize = 12, Foreground = Brush(AppPalette.Muted), Visibility = Visibility.Collapsed };
        public readonly TextBlock outputStatus = Text("连接后启用游戏输出 · Xbox 360 / XInput", 11, false, "767676");
        public readonly Border outputDot = new() { Width = 5, Height = 5, CornerRadius = new(3), Background = Brush("AAAAAA"), VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock inputText = Text("等待手柄连接", 12, false, AppPalette.Muted);
        public ControllerFaceView? Model;
        public ControllerFaceView controllerView => Model ??= new();
        public readonly StickPlot leftPlot = new(), rightPlot = new();
        public readonly TextBlock leftValues = Text("X  —    Y  —", 12);
        public readonly TextBlock rightValues = Text("X  —    Y  —", 12);
        public readonly TextBlock leftOutput = Text("输出  — / —", 11, false, AppPalette.Muted);
        public readonly TextBlock rightOutput = Text("输出  — / —", 11, false, AppPalette.Muted);
        public readonly TextBlock leftOffset = Text("中心偏移  —", 11, false, AppPalette.Muted);
        public readonly TextBlock rightOffset = Text("中心偏移  —", 11, false, AppPalette.Muted);
        public readonly ProgressBar leftTrigger = new() { Maximum = 255, Height = 6, Value = 0 };
        public readonly ProgressBar rightTrigger = new() { Maximum = 255, Height = 6, Value = 0 };
        public readonly TextBlock leftTriggerState = Text("等待输入", 12, false, AppPalette.Muted);
        public readonly TextBlock rightTriggerState = Text("等待输入", 12, false, AppPalette.Muted);
        public readonly TextBlock xboxButtons = Text("—", 12);
        public readonly TextBox logBox = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 190, MaxHeight = 260, FontFamily = new("Cascadia Mono"), FontSize = 11 };
        public readonly Button connectButton = new() { Content = "连接手柄", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 44 };
        public readonly Dictionary<ButtonLayout, RadioButton> layoutOptions = new();
        public readonly Dictionary<ButtonLayout, Border> layoutCards = new();
        public readonly TextBlock gameModeTitle = Text("通用兼容模式", 13, true);
        public readonly TextBlock gameModeHelp = Text("支持原生触觉的游戏，可到设置中试用增强模式。", 12, false, AppPalette.Muted);
    }
    private sealed record CachedPage(StackPanel Content, LiveControls Live, string Title, string Subtitle);
    private void WireLiveControls()
    {
        connectButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        connectButton.Click += async (_, _) => await ToggleConnectionAsync();
    }
    private void TryBuildPage()
    {
        try { BuildPage(); }
        catch (Exception error) {
            pageFailed = true;
            AppLog.Write("error", "构建页面 " + selectedPage + ": " + error);
            foreach (var cached in cachedPages.Values) cached.Content.Visibility = Visibility.Collapsed;
            if (pageError is not null) pageHost.Children.Remove(pageError);
            var retry = new Button { Content = "重新打开此页", HorizontalAlignment = HorizontalAlignment.Left };
            retry.Click += (_, _) => TryBuildPage();
            pageError = new StackPanel { Spacing = 14, Children = {
                Text("此页面暂时无法显示", 20, true),
                Text("后台连接继续运行。可以重试此页，或切换到其他页面。", 13), retry } };
            pageHost.Children.Add(pageError);
        }
    }
    private void BuildPage()
    {
        pageFailed = false;
        if (pageError is not null) { pageHost.Children.Remove(pageError); pageError = null; }
        if (cachedPages.TryGetValue(selectedPage, out var cached)) {
            page = cached.Content; live = cached.Live; title.Text = cached.Title; subtitle.Text = cached.Subtitle;
        } else {
            page = new StackPanel { Spacing = 22 }; live = new LiveControls(); WireLiveControls();
            if (selectedPage is "overview" or "test") controllerView.Height = selectedPage == "test" ? 440 : 330;
            switch (selectedPage) {
                case "setup": BuildSetupPage(); break;
                case "test": BuildTestPage(); break;
                case "mapping": BuildMappingPage(); break;
                case "settings": BuildSettingsPage(); break;
                default: BuildOverview(); break;
            }
            cachedPages.Add(selectedPage, new(page, live, title.Text, subtitle.Text));
            pageHost.Children.Add(page);
        }
        foreach (var entry in cachedPages) entry.Value.Content.Visibility = entry.Key == selectedPage ? Visibility.Visible : Visibility.Collapsed;
        if (selectedPage == "settings") { logBox.Text = string.Join(Environment.NewLine, logLines); RefreshAddressEditor(); }
        UpdateStatus(controller.Info); UpdateLiveInput();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => pageScroll.ChangeView(null, 0, null, true));
    }

    private void BuildOverview()
    {
        title.Text = "连接总览"; subtitle.Text = "连接 PC，随时回到主机。";
        var hero = new StackPanel { Spacing = 8 };
        var heroHeader = new Grid(); heroHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heroHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heroHeader.Children.Add(Text("你的手柄", 12, true, AppPalette.Muted));
        var statusBadge = SyncHint.ForText(badge);
        Grid.SetColumn(statusBadge, 1); heroHeader.Children.Add(statusBadge); hero.Children.Add(heroHeader);
        hero.Children.Add(Text("Switch 2 Pro Controller", 23, true)); hero.Children.Add(addressText);
        hero.Children.Add(controllerView); hero.Children.Add(SyncHint.ForText(statusText));
        var actions = new StackPanel { Spacing = 15 };
        actions.Children.Add(Text("无线连接", 18, true));
        actions.Children.Add(SyncHint.ForText(live.connectionHint));
        actions.Children.Add(connectButton);
        actions.Children.Add(new Border { Height = 1, Background = Brush("EEEEEE"), Margin = new(0, 5, 0, 3) });
        actions.Children.Add(Text("保留 NS2 配对", 13, true));
        actions.Children.Add(Text("回 NS2：先断开 PC，再按 HOME。", 12, false, AppPalette.Muted));
        actions.Children.Add(new Border { Height = 1, Background = Brush("EEEEEE"), Margin = new(0, 5, 0, 3) });
        actions.Children.Add(live.gameModeTitle); actions.Children.Add(live.gameModeHelp);
        var modeSettings = new HyperlinkButton { Content = "设置游戏模式", Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        modeSettings.Click += (_, _) => OpenEnhancementSettings(); actions.Children.Add(modeSettings);
        page.Children.Add(Columns(Card(hero, 24), Card(actions, 23), 1.8, 1));
        var battery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13 };
        live.batteryIcon.VerticalAlignment = VerticalAlignment.Center;
        battery.Children.Add(live.batteryIcon); battery.Children.Add(new StackPanel { Spacing = 2, Children = { batteryText, live.batteryState } });
        page.Children.Add(Columns(Metric("输入频率", hzText), Metric("手柄电量", battery), 1, 1));
        page.Children.Add(Card(MappingPreview(), 16));
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(3, 0, 0, 2),
            Children = { live.outputDot, live.outputStatus } });
    }

    private void BuildTestPage()
    {
        title.Text = "手柄测试"; subtitle.Text = "每一次按下，都看得见。";
        var instructions = new StackPanel { Spacing = 14, Children = { Text("输入状态", 14, true), SyncHint.ForText(badge), SyncHint.ForText(statusText), connectButton,
            Text("先在本应用连接手柄，再按键或拨动摇杆，检测会自动开始。其他连接工具应先断开这只手柄。", 12, false, AppPalette.Muted),
            Text("游戏输入 · Xbox 键名", 12, true), xboxButtons } };
        var model = new StackPanel { Spacing = 8, Children = { controllerView,
            Text("按键亮起 · 摇杆随输入移动", 11, false, AppPalette.Muted), inputText } };
        page.Children.Add(Columns(Card(model, 12), Card(instructions, 20), 2.15, 1));
        var trail = new CheckBox { Content = "显示摇杆轨迹", IsChecked = leftPlot.ShowTrail };
        trail.Checked += (_, _) => leftPlot.ShowTrail = rightPlot.ShowTrail = true;
        trail.Unchecked += (_, _) => leftPlot.ShowTrail = rightPlot.ShowTrail = false;
        var clear = new Button { Content = "清空轨迹" };
        clear.Click += (_, _) => { leftPlot.Clear(); rightPlot.Clear(); };
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { trail, clear,
            Text("● 原始输入    ○ 死区处理后输出", 12, false, AppPalette.Muted) } });
        page.Children.Add(Columns(StickCard("左摇杆", leftPlot, leftValues, leftOutput, leftOffset), StickCard("右摇杆", rightPlot, rightValues, rightOutput, rightOffset), 1, 1));
        page.Children.Add(Columns(TriggerCard("ZL · 左扳机", leftTrigger, live.leftTriggerState), TriggerCard("ZR · 右扳机", rightTrigger, live.rightTriggerState), 1, 1));
        page.Children.Add(Text("中心偏移请在松开摇杆后观察；数字扳机只有按下和松开两种状态。", 12, false, AppPalette.Muted));
        page.Children.Add(RumbleTestCard());
    }

    private void BuildMappingPage()
    {
        title.Text = "按键映射"; subtitle.Text = "选择 ABXY 布局，立即应用到游戏输出。";
        page.Children.Add(Columns(LayoutCard(ButtonLayout.Labels), LayoutCard(ButtonLayout.Positions), 1, 1));
        var detail = new StackPanel { Spacing = 14, Children = { Text("通用映射", 17, true), Text("NS2Pro 手柄  →  Xbox 游戏输出", 11, false, AppPalette.Muted) } };
        detail.Children.Add(Columns(MappingTile("肩键", Keycaps(false, false, "L", "R"), Keycaps(true, false, "LB", "RB")),
            MappingTile("扳机", Keycaps(false, false, "ZL", "ZR"), Keycaps(true, false, "LT", "RT")), 1, 1));
        detail.Children.Add(Columns(MappingTile("菜单", Keycaps(false, false, "+", "−"), Keycaps(true, false, "Start", "Back")),
            MappingTile("摇杆按下", Keycaps(false, true, "L↓", "R↓"), Keycaps(true, true, "LS", "RS")), 1, 1));
        detail.Children.Add(Columns(MappingTile("十字键", DirectionPad(false), DirectionPad(true)),
            MappingTile("双摇杆", Keycaps(false, true, "L", "R"), Keycaps(true, true, "L", "R")), 1, 1));
        detail.Children.Add(Text("HOME、截图、C、GL / GR 仅用于输入检测，暂未映射到 Xbox 输出。", 11, false, AppPalette.Muted));
        page.Children.Add(Card(detail, 24));
        var deadzone = new StackPanel { Spacing = 10, Children = { Text("摇杆死区", 17, true), Text("轻微中心偏移由死区过滤；可以到手柄测试页查看处理前后的差别。", 12, false, AppPalette.Muted) } };
        var value = Text($"{settings.Deadzone:P0}", 14, true, AppPalette.AccentText);
        var slider = new Slider { Minimum = 0, Maximum = 30, StepFrequency = 1, Value = settings.Deadzone * 100, Header = "中心死区（0–30%）" };
        slider.ValueChanged += (_, args) => { settings.Deadzone = args.NewValue / 100; value.Text = $"{settings.Deadzone:P0}"; SaveSettings(); };
        deadzone.Children.Add(value); deadzone.Children.Add(slider); page.Children.Add(Card(deadzone, 24));
    }

    private void BuildSettingsPage()
    {
        title.Text = "设置与诊断"; subtitle.Text = "保持简单，也保留需要时的排查工具。";
        page.Children.Add(HapticBackendOptions());
        page.Children.Add(RumbleSettingsCard());
        var behavior = new StackPanel { Spacing = 18, Children = { Text("运行方式", 17, true) } };
        var trayToggle = new ToggleSwitch { Header = "关闭窗口后继续在后台运行", OnContent = "缩至托盘，保持连接", OffContent = "关闭窗口即断开并退出", IsOn = settings.CloseToTray };
        trayToggle.Toggled += (_, _) => { settings.CloseToTray = trayToggle.IsOn; SaveSettings(); };
        var auto = new ToggleSwitch { Header = "启动应用时开始等待手柄", OnContent = SyncHint.ForText(Text("等待 SYNC 广播")), OffContent = "手动点击连接", IsOn = settings.AutoStartConnection };
        auto.Toggled += (_, _) => { settings.AutoStartConnection = auto.IsOn; SaveSettings(); };
        behavior.Children.Add(trayToggle); behavior.Children.Add(auto);
        behavior.Children.Add(Text("需要保持连接时，也可使用窗口的最小化按钮。后台运行开启后，可从托盘菜单完全退出。", 12, false, AppPalette.Muted));
        page.Children.Add(Card(behavior, 24));
        page.Children.Add(UpdateSettingsCard());
        var advanced = new StackPanel { Spacing = 14, Children = {
            Text("应用会自动记住连接过的手柄。只有指定其他手柄或排查连接问题时才需要修改；留空可重新发现。", 12, false, AppPalette.Muted) } };
        var target = new TextBox { Header = "指定手柄蓝牙地址", Text = settings.LastControllerAddress, PlaceholderText = "留空时自动发现正在等待配对的 Pro 手柄",
            Description = SyncHint.ForText(Text("长按 SYNC，让手柄进入配对广播状态。", 12, false, AppPalette.Muted)) };
        addressEditor = target; addressDirty = false;
        var addressStatus = Text("", 12, false, AppPalette.Muted); addressStatus.Visibility = Visibility.Collapsed;
        var apply = new Button { Content = "保存手柄地址", IsEnabled = false };
        target.TextChanged += (_, _) => {
            if (updatingAddress) return;
            addressDirty = true; apply.IsEnabled = true; addressStatus.Visibility = Visibility.Collapsed;
        };
        apply.Click += (_, _) => {
            var text = target.Text.Trim().ToUpperInvariant();
            if (text.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(text, "^([0-9A-F]{2}:){5}[0-9A-F]{2}$")) {
                addressStatus.Text = "请使用 AA:BB:CC:DD:EE:FF 格式，或留空以发现新手柄。";
                addressStatus.Foreground = Brush(AppPalette.AccentText); addressStatus.Visibility = Visibility.Visible; return;
            }
            settings.LastControllerAddress = text; SaveSettings();
            addressDirty = false; RefreshAddressEditor(); apply.IsEnabled = false;
            addressStatus.Text = "已保存，下次查找手柄时生效。";
            addressStatus.Foreground = Brush(AppPalette.Muted); addressStatus.Visibility = Visibility.Visible;
        };
        advanced.Children.Add(target); advanced.Children.Add(apply); advanced.Children.Add(addressStatus);
        page.Children.Add(new Expander { Header = "高级连接选项", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = advanced });
        var diagnostics = new StackPanel { Spacing = 14, Children = { Text("连接诊断", 17, true) } };
        var result = Text("可检查蓝牙 LE 和 ViGEmBus 驱动。", 12, false, AppPalette.Muted);
        var check = new Button { Content = "检查运行环境" };
        check.Click += async (_, _) => {
            check.IsEnabled = false;
            try {
                var report = await PrerequisiteService.CheckAsync();
                result.Text = string.Join("\n", report.Items.Select(item => (item.Ready ? "✓ " : "○ ") + item.Name + "：" + item.Detail));
                AppLog.Write("info", result.Text);
            } catch (Exception error) { result.Text = error.Message; AppLog.Write("error", error.Message); }
            finally { check.IsEnabled = true; }
        };
        var logs = new Button { Content = "打开日志文件夹" };
        logs.Click += (_, _) => {
            try { Directory.CreateDirectory(AppSettings.DataDirectory); Process.Start(new ProcessStartInfo(AppSettings.DataDirectory) { UseShellExecute = true }); }
            catch (Exception error) { result.Text = "未能打开日志文件夹：" + error.Message; }
        };
        var copy = new Button { Content = "复制诊断信息" };
        copy.Click += (_, _) => {
            try {
                var package = new DataPackage();
                package.SetText($"{AppIdentity.DisplayName} v{AppVersion}\nWindows: {Environment.OSVersion}\n状态: {controller.Info}\n预设: {settings.Layout}\n死区: {settings.Deadzone:P0}\n\n" + string.Join(Environment.NewLine, logLines));
                Clipboard.SetContent(package); result.Text = "诊断信息已复制";
            } catch (Exception error) { result.Text = "未能复制诊断信息：" + error.Message; }
        };
        var tools = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 3, ItemWidth = 150, ItemHeight = 44 };
        foreach (var tool in new[] { check, logs, copy }) { tool.Margin = new(0, 0, 8, 8); tools.Children.Add(tool); }
        diagnostics.Children.Add(tools);
        diagnostics.Children.Add(result); logBox.Text = string.Join(Environment.NewLine, logLines); diagnostics.Children.Add(logBox);
        page.Children.Add(Card(diagnostics, 24));
        page.Children.Add(Text("支持按键、摇杆、电量、充电状态及通用游戏震动。原生触觉增强仍在验证；体感和耳机音频暂未支持。", 12, false, AppPalette.Muted));
    }

    private void RefreshAddressEditor()
    {
        if (addressEditor is null || addressDirty) return;
        updatingAddress = true;
        try { addressEditor.Text = settings.LastControllerAddress; }
        finally { updatingAddress = false; }
    }

    private async Task ToggleConnectionAsync()
    {
        try {
            if (controller.Info.CanStop)
                await controller.StopAsync();
            else await BeginConnectionAsync();
        } catch (Exception error) { await ShowInfoAsync("连接操作未完成", error.Message); }
    }
    private async Task BeginConnectionAsync()
    {
        if (exiting || startingConnection || !controller.Info.CanStart) return;
        startingConnection = true;
        try {
            // Pending optional setup must never block an ordinary connection.
            if (enhancement.Status.Busy && enhancement.Status.Phase != EnhancementPhase.InstallingDriver) {
                enhancement.Cancel(); await enhancement.WaitForIdleAsync();
            }
            if (settings.OutputMode == GameOutputMode.DualSenseExperimental) {
                if (await enhancement.PrepareAsync(settings)) { await controller.StartAsync(); return; }
                settings.OutputMode = GameOutputMode.Xbox; settings.PreferNativeHaptics = false; wantsEnhanced = false;
                enhancementFallback = "增强暂未就绪，本次使用通用兼容。可到设置中继续准备增强环境。";
                if (gameMode is not null) gameMode.SelectedIndex = 0;
                SaveSettings();
            }
            var report = await PrerequisiteService.CheckAsync();
            if (exiting || !controller.Info.CanStart) return;
            if (!report.Ready) {
                NativeMethods.ShowExisting(WinRT.Interop.WindowNative.GetWindowHandle(this)); Activate();
                SelectPage("setup");
                if (connectionGuide is not null) await connectionGuide.RefreshEnvironmentAsync();
                return;
            }
            await controller.StartAsync();
        } finally { startingConnection = false; lastEnhancementStatus = null; UpdateEnhancementUi(); UpdateOverviewModeHint(); }
    }
    private async Task SetTrayConnectionAsync(bool connect)
    {
        if (exiting) return;
        try {
            if (connect) await BeginConnectionAsync();
            else await controller.StopAsync();
        } catch (Exception error) {
            NativeMethods.ShowExisting(WinRT.Interop.WindowNative.GetWindowHandle(this)); Activate();
            await ShowInfoAsync("连接操作未完成", error.Message);
        }
    }
    private void UpdateStatus(ConnectionInfo info)
    {
        tray?.Update(info.Phase);
        if (selectedPage == "settings") RefreshAddressEditor();
        connectionGuide?.Update(info, controller.Input);
        if (pageFailed || !cachedPages.ContainsKey(selectedPage)) return;
        UpdateMappingSummary();
        UpdateOverviewModeHint();
        badge.Text = info.Phase switch { ConnectionPhase.Connected => "● 已连接", ConnectionPhase.WaitingForInput => "● 等待输入", ConnectionPhase.Searching => "● 等待 SYNC", ConnectionPhase.Connecting => "● 连接中", ConnectionPhase.Preparing => "● 正在准备", ConnectionPhase.Stopping => "● 正在断开", ConnectionPhase.Error => "● 连接未完成", _ => "○ 未连接" };
        badge.Foreground = Brush(info.Phase == ConnectionPhase.Connected ? AppPalette.AccentText : info.Phase is ConnectionPhase.Error or ConnectionPhase.WaitingForInput ? "AC6635" : AppPalette.Muted);
        statusText.Text = info.Message;
        statusText.Visibility = string.IsNullOrWhiteSpace(info.Message) ? Visibility.Collapsed : Visibility.Visible;
        addressText.Text = info.Address is null ? "蓝牙 LE · 保留 NS2 配对" : info.Address + " · 蓝牙 LE";
        connectButton.Content = info.ActionText;
        connectButton.IsEnabled = info.CanStart || info.CanStop;
        live.connectionHint.Text = info.Phase switch {
            ConnectionPhase.Connected => "连接已建立，可直接开始游戏。",
            ConnectionPhase.WaitingForInput => "蓝牙已连接，正在等待手柄输入。",
            ConnectionPhase.Searching => "长按顶部 SYNC，直到手柄指示灯循环闪烁。",
            ConnectionPhase.Preparing or ConnectionPhase.Connecting => "正在连接手柄，请稍候。",
            ConnectionPhase.Stopping => "正在释放连接，请稍候。",
            ConnectionPhase.Error => "点击重试连接，再长按手柄顶部 SYNC。",
            _ => "先点击连接，再长按手柄顶部的 SYNC 小键。"
        };
        hzText.Text = info.Phase == ConnectionPhase.Connected ? $"{info.Hertz:F0} Hz" : "—";
        var outputState = info.Phase switch {
            ConnectionPhase.Connected => "游戏输出已就绪",
            ConnectionPhase.WaitingForInput => "游戏输出等待手柄输入",
            ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting => "游戏输出等待连接",
            ConnectionPhase.Stopping => "正在释放游戏输出",
            ConnectionPhase.Error => "游戏输出未就绪",
            _ => "连接后启用游戏输出"
        };
        live.outputStatus.Text = outputState + " · " + controller.OutputName;
        live.outputDot.Background = Brush(info.Phase == ConnectionPhase.Connected ? AppPalette.Accent : "AAAAAA");
    }
    private void UpdateLiveInput()
    {
        UpdateRumbleStatus();
        UpdateEnhancementUi();
        bool hasInput = controller.Input is not null;
        if (!hasInput && hadLiveInput) {
            foreach (var cached in cachedPages.Values) { cached.Live.leftPlot.Clear(); cached.Live.rightPlot.Clear(); }
        }
        hadLiveInput = hasInput;
        if (!settings.HasCompletedFirstConnection && controller.Input is not null) {
            settings.HasCompletedFirstConnection = true; SaveSettings();
        }
        connectionGuide?.Update(controller.Info, controller.Input);
        if (pageFailed || !cachedPages.ContainsKey(selectedPage)) return;
        var state = controller.Input;
        if (selectedPage is "overview" or "test") controllerView.Update(state);
        batteryText.Text = state is null ? "—" : $"{state.BatteryPercent}%";
        live.batteryIcon.Update(state?.BatteryPercent, state?.IsCharging ?? false, state?.HasExternalPower ?? false);
        live.batteryState.Text = state?.IsCharging == true ? "正在充电" : "已接电源，未充电";
        live.batteryState.Visibility = state is { IsCharging: true } or { HasExternalPower: true } ? Visibility.Visible : Visibility.Collapsed;
        inputText.Text = state is null ? "尚未接收输入 · 连接后自动开始检测" : $"● 正在接收 · {controller.Info.Hertz:F1} Hz · {controller.Info.Reports:N0} 份报告";
        leftPlot.Update(state?.UnfilteredLeft, state?.Left, settings.Deadzone); rightPlot.Update(state?.UnfilteredRight, state?.Right, settings.Deadzone);
        live.leftTriggerState.Text = state is null ? "等待输入" : state.Xbox.LeftTrigger > 0 ? "已按下" : "未按下";
        live.rightTriggerState.Text = state is null ? "等待输入" : state.Xbox.RightTrigger > 0 ? "已按下" : "未按下";
        leftTrigger.Value = state?.Xbox.LeftTrigger ?? 0; rightTrigger.Value = state?.Xbox.RightTrigger ?? 0;
        xboxButtons.Text = state is null ? "等待输入" : XboxLabels(state.Xbox);
        if (state is null) {
            leftValues.Text = rightValues.Text = "X  —    Y  —";
            leftOutput.Text = rightOutput.Text = "输出  — / —";
            leftOffset.Text = rightOffset.Text = "中心偏移  —";
            return;
        }
        var left = state.UnfilteredLeft; var right = state.UnfilteredRight;
        var leftFiltered = state.Left; var rightFiltered = state.Right;
        leftValues.Text = $"X  {left.X:+0.000;-0.000;+0.000}    Y  {left.Y:+0.000;-0.000;+0.000}";
        rightValues.Text = $"X  {right.X:+0.000;-0.000;+0.000}    Y  {right.Y:+0.000;-0.000;+0.000}";
        leftOutput.Text = $"输出  {leftFiltered.X:F3} / {leftFiltered.Y:F3}"; rightOutput.Text = $"输出  {rightFiltered.X:F3} / {rightFiltered.Y:F3}";
        leftOffset.Text = $"中心偏移  {Math.Sqrt(left.X * left.X + left.Y * left.Y) * 100:F1}%";
        rightOffset.Text = $"中心偏移  {Math.Sqrt(right.X * right.X + right.Y * right.Y) * 100:F1}%";
    }
    private static string XboxLabels(XboxReport input)
    {
        var pressed = new List<string>();
        foreach (var (mask, label) in new (ushort, string)[] { (0x1000,"A"),(0x2000,"B"),(0x4000,"X"),(0x8000,"Y"),(0x100,"LB"),(0x200,"RB"),(0x10,"Start"),(0x20,"Back"),(0x40,"LS"),(0x80,"RS"),(1,"↑"),(2,"↓"),(4,"←"),(8,"→") })
            if ((input.Buttons & mask) != 0) pressed.Add(label);
        if (input.LeftTrigger > 0) pressed.Add("LT"); if (input.RightTrigger > 0) pressed.Add("RT");
        return pressed.Count == 0 ? "未按下按键" : string.Join("  ·  ", pressed);
    }
    private void ChangeLayout(ButtonLayout layout)
    {
        if (settings.Layout == layout) return;
        settings.Layout = layout; SaveSettings(); UpdateMappingSummary();
        AppLog.Write("info", "映射预设：" + (layout == ButtonLayout.Labels ? "Nintendo 布局 · 字母对应" : "Xbox 布局 · 位置对应"));
    }
    private void SaveSettings() { try { settings.Save(); } catch (Exception error) { AppLog.Write("error", "保存设置失败：" + error.Message); } }
    private void SelectPage(string tag) => navigation.SelectedItem = navigation.MenuItems.Concat(navigation.FooterMenuItems)
        .OfType<NavigationViewItem>().First(x => Equals(x.Tag, tag));
    private void LogWritten(string line) => DispatcherQueue.TryEnqueue(() => { logLines.Enqueue(line); while (logLines.Count > 80) logLines.Dequeue(); if (selectedPage == "settings") logBox.Text = string.Join(Environment.NewLine, logLines); });
    private async Task ShowInfoAsync(string heading, string description) => await new ContentDialog { Title = heading,
        Content = description.Contains("SYNC", StringComparison.OrdinalIgnoreCase) ? (object)SyncHint.ForText(Text(description)) : description,
        CloseButtonText = "知道了", XamlRoot = ((FrameworkElement)Content).XamlRoot }.ShowAsync();
    private async Task ExitAsync()
    {
        if (exiting) return;
        if (enhancement.Status.Phase == EnhancementPhase.InstallingDriver) {
            OpenEnhancementSettings();
            await ShowInfoAsync("增强驱动正在安装", "请等待官方安装器完成后再退出，以便确认安装结果。电脑不会自动重启。"); return;
        }
        exiting = true;
        try { enhancement.Cancel(); await controller.StopAsync(); await enhancement.DisposeAsync(); timer.Stop(); tray?.Dispose(); exitAllowed = true; Close(); }
        catch (Exception error) {
            exiting = exitAllowed = false;
            NativeMethods.ShowExisting(WinRT.Interop.WindowNative.GetWindowHandle(this)); Activate();
            AppLog.Write("error", "退出未完成：" + error);
            await ShowInfoAsync("退出未完成", error.Message);
        }
    }

    private Border LayoutCard(ButtonLayout layout)
    {
        bool nintendo = layout == ButtonLayout.Labels;
        var choice = new RadioButton { Content = nintendo ? "Nintendo 布局" : "Xbox 布局", GroupName = "LayoutMapping", IsChecked = settings.Layout == layout, FontSize = 18 };
        live.layoutOptions.Add(layout, choice);
        AutomationProperties.SetName(choice, nintendo ? "Nintendo 布局，字母对应，A 对应 A" : "Xbox 布局，位置对应，A 对应 B");
        var content = new StackPanel { Spacing = 15, Children = { choice,
            Text(nintendo ? "字母对应" : "位置对应", 12, true, AppPalette.AccentText) } };
        var diagrams = new Grid { ColumnSpacing = 6 };
        diagrams.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        diagrams.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        diagrams.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        diagrams.Children.Add(ButtonDiamond(false, "手柄"));
        var arrow = Text("→", 22, false, "767676"); arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow, 1); diagrams.Children.Add(arrow);
        var output = ButtonDiamond(!nintendo, "游戏输出"); Grid.SetColumn(output, 2); diagrams.Children.Add(output);
        content.Children.Add(diagrams);
        var card = Card(content, 22);
        live.layoutCards.Add(layout, card);
        card.BorderBrush = Brush(settings.Layout == layout ? AppPalette.Accent : AppPalette.Border);
        card.Background = Brush(settings.Layout == layout ? AppPalette.Soft : "FFFFFF");
        choice.Checked += (_, _) => ChangeLayout(layout);
        card.Tapped += (_, _) => choice.IsChecked = true;
        return card;
    }

    private static StackPanel ButtonDiamond(bool xbox, string label)
    {
        var grid = new Grid { Width = 110, Height = 110, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 3; i++) { grid.RowDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); }
        var letters = xbox ? new[] { "Y", "X", "B", "A" } : new[] { "X", "Y", "A", "B" };
        var coordinates = new[] { (0, 1), (1, 0), (1, 2), (2, 1) };
        for (int i = 0; i < 4; i++) {
            var text = Text(letters[i], 13, true, AppPalette.AccentText); text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            var key = new Border { Width = 31, Height = 31, CornerRadius = new(16), Background = Brush(AppPalette.SoftStrong), Child = text };
            Grid.SetRow(key, coordinates[i].Item1); Grid.SetColumn(key, coordinates[i].Item2); grid.Children.Add(key);
        }
        return new StackPanel { Spacing = 10, Children = { grid, new TextBlock { Text = label, FontSize = 11, Foreground = Brush(AppPalette.Muted), HorizontalAlignment = HorizontalAlignment.Center } } };
    }

    private static Border MappingTile(string label, FrameworkElement source, FrameworkElement output)
    {
        var bridge = new Grid { ColumnSpacing = 12, Margin = new(0, 7, 0, 0) };
        bridge.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        bridge.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bridge.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        source.HorizontalAlignment = output.HorizontalAlignment = HorizontalAlignment.Center;
        bridge.Children.Add(source);
        var arrow = new FontIcon { Glyph = "\uE72A", FontFamily = new("Segoe Fluent Icons"), FontSize = 17, Foreground = Brush("999999"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1); bridge.Children.Add(arrow); Grid.SetColumn(output, 2); bridge.Children.Add(output);
        return new Border { Background = Brush("F7F7F7"), CornerRadius = new(12), Padding = new(16, 13, 16, 15),
            Child = new StackPanel { Spacing = 4, Children = { Text(label, 12, true, "666666"), bridge } } };
    }

    private static StackPanel Keycaps(bool output, bool round, params string[] labels)
    {
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var label in labels) {
            var text = Text(label, label.Length > 3 ? 11 : 13, true, output ? "FFFFFF" : "454545");
            text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            var key = new Border { Width = label.Length > 3 ? 54 : 40, Height = 36, CornerRadius = new(round ? 18 : 8),
                Background = Brush(output ? AppPalette.Accent : "FFFFFF"), BorderBrush = Brush(output ? AppPalette.Accent : "DEDEDE"), BorderThickness = new(1), Child = text };
            pair.Children.Add(key);
        }
        return pair;
    }

    private static Viewbox DirectionPad(bool output)
    {
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas { Width = 44, Height = 44 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M 16 2 Q 14 2 14 4 L 14 14 L 4 14 Q 2 14 2 16 L 2 28 Q 2 30 4 30 L 14 30 L 14 40 Q 14 42 16 42 L 28 42 Q 30 42 30 40 L 30 30 L 40 30 Q 42 30 42 28 L 42 16 Q 42 14 40 14 L 30 14 L 30 4 Q 30 2 28 2 Z'/>");
        path.Fill = Brush(output ? AppPalette.Accent : "FFFFFF"); path.Stroke = Brush(output ? AppPalette.Accent : "DEDEDE"); path.StrokeThickness = 1;
        canvas.Children.Add(path);
        var center = Text("+", 19, true, output ? "FFFFFF" : "666666"); center.Width = center.Height = 44; center.TextAlignment = TextAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center; center.Padding = new(0, 7, 0, 0); canvas.Children.Add(center);
        return new Viewbox { Width = 44, Height = 40, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private void UpdateMappingSummary()
    {
        foreach (var cached in cachedPages.Values) {
            foreach (var (layout, option) in cached.Live.layoutOptions) option.IsChecked = settings.Layout == layout;
            foreach (var (layout, card) in cached.Live.layoutCards) {
                bool selected = settings.Layout == layout;
                card.BorderBrush = Brush(selected ? AppPalette.Accent : AppPalette.Border);
                card.Background = Brush(selected ? AppPalette.Soft : "FFFFFF");
            }
        }
    }
    private Grid MappingPreview()
    {
        var row = new Grid { ColumnSpacing = 20 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Text("当前映射", 14, true); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var layout in new[] { ButtonLayout.Labels, ButtonLayout.Positions }) {
            bool nintendo = layout == ButtonLayout.Labels;
            var option = new RadioButton { Content = nintendo ? "Nintendo 布局" : "Xbox 布局", GroupName = "LayoutOverview",
                Style = (Style)Application.Current.Resources["LayoutPresetOptionStyle"], IsChecked = settings.Layout == layout };
            AutomationProperties.SetName(option, nintendo ? "Nintendo 布局，字母对应" : "Xbox 布局，位置对应");
            ToolTipService.SetToolTip(option, nintendo ? "按键字母对应" : "按键位置对应");
            option.Checked += (_, _) => ChangeLayout(layout);
            live.layoutOptions.Add(layout, option); choices.Children.Add(option);
        }
        var group = new Border { Background = Brush("F6F6F6"), CornerRadius = new(12), Padding = new(4), Child = choices };
        Grid.SetColumn(group, 1); row.Children.Add(group);
        bool? stacked = null;
        row.SizeChanged += (_, args) => {
            bool narrow = args.NewSize.Width < 370;
            if (stacked == narrow) return;
            stacked = narrow; row.RowDefinitions.Clear();
            if (narrow) {
                row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetColumnSpan(label, 2); Grid.SetColumn(group, 0); Grid.SetColumnSpan(group, 2); Grid.SetRow(group, 1);
                row.RowSpacing = 12; group.HorizontalAlignment = HorizontalAlignment.Left;
            } else {
                Grid.SetColumnSpan(label, 1); Grid.SetColumnSpan(group, 1); Grid.SetRow(group, 0); Grid.SetColumn(group, 1);
                row.RowSpacing = 0; group.HorizontalAlignment = HorizontalAlignment.Right;
            }
        };
        return row;
    }
    private static Border Metric(string label, FrameworkElement value) => Card(new StackPanel { Spacing = 10, Children = { Text(label, 12, false, AppPalette.Muted), value } }, 20);
    private static Border StickCard(string label, StickPlot plot, TextBlock values, TextBlock output, TextBlock offset) => Card(new StackPanel { Spacing = 8, Children = { Text(label, 15, true), plot, values, output, offset } }, 20);
    private static Border TriggerCard(string label, ProgressBar bar, TextBlock state) => Card(new StackPanel { Spacing = 12, Children = { Text(label, 13, true), bar, state } }, 20);
    private static Grid Columns(FrameworkElement first, FrameworkElement second, double a, double b, FrameworkElement? third = null)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new() { Width = new(a, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(b, GridUnitType.Star) });
        grid.Children.Add(first); Grid.SetColumn(second, 1); grid.Children.Add(second);
        if (third is not null) { grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.SetColumn(third, 2); grid.Children.Add(third); }
        bool? stacked = null;
        grid.SizeChanged += (_, args) => {
            bool narrow = args.NewSize.Width < 680;
            if (stacked == narrow) return;
            stacked = narrow;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            var children = third is null ? new[] { first, second } : new[] { first, second, third };
            if (narrow) {
                grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.RowSpacing = 18;
                for (int i = 0; i < children.Length; i++) {
                    grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetColumn(children[i], 0); Grid.SetRow(children[i], i);
                }
            } else {
                grid.RowSpacing = 0;
                foreach (double width in third is null ? new[] { a, b } : new[] { a, b, 1.0 }) grid.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
                for (int i = 0; i < children.Length; i++) { Grid.SetColumn(children[i], i); Grid.SetRow(children[i], 0); }
            }
        };
        return grid;
    }
    private static Border Card(UIElement child, double padding) => new() { Background = Brush("FFFFFF"), BorderBrush = Brush(AppPalette.Border), BorderThickness = new(1), CornerRadius = new(16), Padding = new(padding), Child = child };
    private static TextBlock Text(string text, double size = 14, bool bold = false, string color = AppPalette.Text) => new() {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(color), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
    private static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
}
