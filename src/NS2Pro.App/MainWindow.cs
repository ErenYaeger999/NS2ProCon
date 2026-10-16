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

public sealed class MainWindow : Window
{
    private static readonly string AppVersion = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private readonly AppSettings settings = AppSettings.Load();
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
    private readonly TextBlock subtitle = Text("连接 PC，随时回到主机。", 13, false, "78858B");
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
    private Button returnButton => live.returnButton;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private TrayIcon? tray;
    private bool exiting, exitAllowed;
    private string selectedPage = "overview";
    private readonly Queue<string> logLines = new();

    public MainWindow()
    {
        controller = new(settings);
        Title = "NS2 Pro";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1200, 870));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        var root = new Grid { RequestedTheme = ElementTheme.Light, Background = Brush("F6F8F8") };
        root.RowDefinitions.Add(new() { Height = new(44) });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var titlebar = new Grid { Margin = new(20, 0, 150, 0) };
        var appName = Text("NS2 Pro", 12, true, "516168");
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
        var quit = new Button { Content = "退出应用", FontSize = 11, Padding = new(10, 5, 10, 5) };
        quit.Click += async (_, _) => await ExitAsync();
        navigation.PaneFooter = new StackPanel { Margin = new(20, 0, 0, 20), Spacing = 10, Children = { quit, Text($"v{AppVersion}", 11, false, "8A969B") } };
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
        content.Children.Add(heading); Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        navigation.Content = content; Grid.SetRow(navigation, 1); root.Children.Add(navigation); Content = root;
        controller.Changed += info => DispatcherQueue.TryEnqueue(() => UpdateStatus(info));
        AppLog.LineWritten += LogWritten;
        if (File.Exists(AppLog.FilePath)) foreach (var line in File.ReadLines(AppLog.FilePath).TakeLast(30)) logLines.Enqueue(line);
        timer.Tick += (_, _) => UpdateLiveInput(); timer.Start();
        AppWindow.Closing += async (_, args) => {
            if (exitAllowed) return;
            args.Cancel = true;
            if (settings.MinimizeToTray && tray is not null) NativeMethods.ShowWindow(hwnd, 0);
            else await ExitAsync();
        };
        Closed += (_, _) => { timer.Stop(); foreach (var cached in cachedPages.Values) cached.Live.Model?.Dispose(); tray?.Dispose(); AppLog.LineWritten -= LogWritten; };
        try { tray = new(hwnd, () => DispatcherQueue.TryEnqueue(() => { NativeMethods.ShowExisting(hwnd); Activate(); }),
            () => DispatcherQueue.TryEnqueue(async () => await ExitAsync())); }
        catch (Exception error) { AppLog.Write("warn", error.Message); }
        bool initialized = false;
        root.Loaded += (_, _) => {
            if (initialized) return;
            initialized = true;
            navigation.SelectedItem = navigation.MenuItems[0];
            AppLog.Write("info", $"NS2 Pro v{AppVersion} 已启动 · WinUI 3");
            if (settings.AutoStartConnection) _ = controller.StartAsync();
        };
    }

    private void AddNavigation(string label, string tag, string glyph) => navigation.MenuItems.Add(new NavigationViewItem {
        Content = label, Tag = tag, Icon = new FontIcon { Glyph = glyph, FontFamily = new("Segoe Fluent Icons") }, Margin = new(8, 3, 8, 3) });
    private static StackPanel Brand() => new() { Margin = new(20, 16, 0, 24), Spacing = 6,
        Children = { new Border { Width = 42, Height = 42, CornerRadius = new(13), Background = Brush("187B78"), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new FontIcon { Glyph = "\uE7FC", FontFamily = new("Segoe Fluent Icons"), FontSize = 22, Foreground = Brush("FFFFFF") } },
            Text("NS2 Pro", 22, true), Text("一只手柄，自在切换", 11, false, "78858B") } };

    // Each page owns its visual tree for its entire lifetime. Never reparent focused buttons,
    // status elements, or a live preview while WinUI is processing navigation/focus events.
    private sealed class LiveControls
    {
        public readonly TextBlock badge = Text("未连接", 12, true, "65777D");
        public readonly TextBlock statusText = Text("准备好连接你的手柄", 14);
        public readonly TextBlock addressText = Text("蓝牙 LE · 保留 NS2 配对", 12, false, "78858B");
        public readonly TextBlock hzText = Text("—", 26, true);
        public readonly TextBlock batteryText = Text("—", 26, true);
        public readonly BatteryIcon batteryIcon = new();
        public readonly TextBlock outputStatus = Text("连接后启用游戏输出 · Xbox 360 / XInput", 11, false, "8A969B");
        public readonly Border outputDot = new() { Width = 5, Height = 5, CornerRadius = new(3), Background = Brush("A4B0B3"), VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock inputText = Text("等待手柄连接", 12, false, "78858B");
        public ControllerFaceView? Model;
        public ControllerFaceView controllerView => Model ??= new();
        public readonly StickPlot leftPlot = new(), rightPlot = new();
        public readonly TextBlock leftValues = Text("X  +0.000    Y  +0.000", 12);
        public readonly TextBlock rightValues = Text("X  +0.000    Y  +0.000", 12);
        public readonly TextBlock leftOutput = Text("输出  0.000 / 0.000", 11, false, "78858B");
        public readonly TextBlock rightOutput = Text("输出  0.000 / 0.000", 11, false, "78858B");
        public readonly TextBlock leftOffset = Text("中心偏移  0.0%", 11, false, "78858B");
        public readonly TextBlock rightOffset = Text("中心偏移  0.0%", 11, false, "78858B");
        public readonly ProgressBar leftTrigger = new() { Maximum = 255, Height = 6, Value = 0 };
        public readonly ProgressBar rightTrigger = new() { Maximum = 255, Height = 6, Value = 0 };
        public readonly TextBlock xboxButtons = Text("—", 12);
        public readonly TextBox logBox = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 190, MaxHeight = 260, FontFamily = new("Cascadia Mono"), FontSize = 11 };
        public readonly Button connectButton = new() { Content = "连接手柄", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 44 };
        public readonly Button returnButton = new() { Content = "切回 NS2", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 42 };
        public readonly List<TextBlock> MappingLabels = new();
    }
    private sealed record CachedPage(StackPanel Content, LiveControls Live, string Title, string Subtitle);
    private void WireLiveControls()
    {
        connectButton.Background = Brush("187B78"); connectButton.Foreground = Brush("FFFFFF");
        connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        returnButton.Click += async (_, _) => { await controller.StopAsync(); await ShowInfoAsync("可以回到 NS2 了", "PC 连接与虚拟手柄已断开。现在按手柄 HOME，正常连接 NS2 即可，无需重新配对。"); };
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
            if (selectedPage is "overview" or "test") controllerView.Height = selectedPage == "test" ? 380 : 270;
            switch (selectedPage) {
                case "test": BuildTestPage(); break;
                case "mapping": BuildMappingPage(); break;
                case "settings": BuildSettingsPage(); break;
                default: BuildOverview(); break;
            }
            cachedPages.Add(selectedPage, new(page, live, title.Text, subtitle.Text));
            pageHost.Children.Add(page);
        }
        foreach (var entry in cachedPages) entry.Value.Content.Visibility = entry.Key == selectedPage ? Visibility.Visible : Visibility.Collapsed;
        if (selectedPage == "settings") logBox.Text = string.Join(Environment.NewLine, logLines);
        UpdateStatus(controller.Info); UpdateLiveInput();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => pageScroll.ChangeView(null, 0, null, true));
    }

    private void BuildOverview()
    {
        title.Text = "连接总览"; subtitle.Text = "连接 PC，随时回到主机。";
        var hero = new StackPanel { Spacing = 8 };
        var heroHeader = new Grid(); heroHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heroHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heroHeader.Children.Add(Text("你的手柄", 12, true, "78858B"));
        Grid.SetColumn(badge, 1); heroHeader.Children.Add(badge); hero.Children.Add(heroHeader);
        hero.Children.Add(Text("Switch 2 Pro Controller", 23, true)); hero.Children.Add(addressText);
        hero.Children.Add(controllerView); hero.Children.Add(statusText);
        var actions = new StackPanel { Spacing = 15 };
        actions.Children.Add(Text("准备好，开始游戏", 18, true));
        actions.Children.Add(Text("先点击连接，再长按手柄顶部的 SYNC 小键。", 13, false, "78858B"));
        actions.Children.Add(connectButton); actions.Children.Add(returnButton);
        actions.Children.Add(new Border { Height = 1, Background = Brush("E7ECEC"), Margin = new(0, 5, 0, 3) });
        actions.Children.Add(Text("保留 NS2 配对", 13, true));
        actions.Children.Add(Text("用 SYNC 进入 PC 模式；结束后按 HOME 回主机。", 12, false, "78858B"));
        actions.Children.Add(Text("避免误唤醒：离线时不要用 HOME 连接 PC。", 12, false, "78858B"));
        page.Children.Add(Columns(Card(hero, 24), Card(actions, 23), 1.8, 1));
        var battery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13 };
        live.batteryIcon.VerticalAlignment = VerticalAlignment.Center;
        battery.Children.Add(live.batteryIcon); battery.Children.Add(batteryText);
        page.Children.Add(Columns(Metric("输入频率", hzText), Metric("手柄电量", battery), 1, 1));
        var preset = new StackPanel { Spacing = 12, Children = { Text("当前映射", 14, true), MappingPreview() } };
        var edit = new Button { Content = "更改预设", HorizontalAlignment = HorizontalAlignment.Right };
        edit.Click += (_, _) => SelectPage("mapping"); preset.Children.Add(edit);
        page.Children.Add(Card(preset, 20));
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(3, 0, 0, 2),
            Children = { live.outputDot, live.outputStatus } });
    }

    private void BuildTestPage()
    {
        title.Text = "手柄测试"; subtitle.Text = "每一次按下，都看得见。";
        var instructions = new StackPanel { Spacing = 14, Children = { Text("输入状态", 14, true), badge, statusText, connectButton,
            Text("需由本应用连接手柄。旧版控制台仍运行时，请先在原窗口按 Ctrl+C 结束连接，再在此连接并长按 SYNC。", 12, false, "78858B"),
            Text("Xbox 游戏输出", 12, true), xboxButtons } };
        var model = new StackPanel { Spacing = 8, Children = { controllerView,
            Text("按键亮起 · 摇杆随输入移动", 11, false, "78858B"), inputText } };
        page.Children.Add(Columns(Card(model, 12), Card(instructions, 20), 2.15, 1));
        var trail = new CheckBox { Content = "显示摇杆轨迹", IsChecked = leftPlot.ShowTrail };
        trail.Checked += (_, _) => leftPlot.ShowTrail = rightPlot.ShowTrail = true;
        trail.Unchecked += (_, _) => leftPlot.ShowTrail = rightPlot.ShowTrail = false;
        var clear = new Button { Content = "清空轨迹" };
        clear.Click += (_, _) => { leftPlot.Clear(); rightPlot.Clear(); };
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { trail, clear,
            Text("● 原始输入    ○ 死区处理后输出", 12, false, "78858B") } });
        page.Children.Add(Columns(StickCard("左摇杆", leftPlot, leftValues, leftOutput, leftOffset), StickCard("右摇杆", rightPlot, rightValues, rightOutput, rightOffset), 1, 1));
        page.Children.Add(Columns(TriggerCard("ZL · 左扳机", leftTrigger), TriggerCard("ZR · 右扳机", rightTrigger), 1, 1));
        page.Children.Add(Text("中心偏移反映校准后、应用死区前的输入；请松开摇杆静置观察。Pro 手柄扳机为数字输入，状态只有 0 或 255。", 12, false, "78858B"));
    }

    private void BuildMappingPage()
    {
        title.Text = "按键映射"; subtitle.Text = "选择 ABXY 布局，立即应用到游戏输出。";
        page.Children.Add(Columns(LayoutCard(ButtonLayout.Labels), LayoutCard(ButtonLayout.Positions), 1, 1));
        var detail = new StackPanel { Spacing = 14, Children = { Text("通用映射", 17, true), Text("NS2 Pro  →  Xbox 游戏输出", 11, false, "78858B") } };
        detail.Children.Add(Columns(MappingTile("肩键", Keycaps(false, false, "L", "R"), Keycaps(true, false, "LB", "RB")),
            MappingTile("扳机", Keycaps(false, false, "ZL", "ZR"), Keycaps(true, false, "LT", "RT")), 1, 1));
        detail.Children.Add(Columns(MappingTile("菜单", Keycaps(false, false, "+", "−"), Keycaps(true, false, "Start", "Back")),
            MappingTile("摇杆按下", Keycaps(false, true, "L↓", "R↓"), Keycaps(true, true, "LS", "RS")), 1, 1));
        detail.Children.Add(Columns(MappingTile("十字键", DirectionPad(false), DirectionPad(true)),
            MappingTile("双摇杆", Keycaps(false, true, "L", "R"), Keycaps(true, true, "L", "R")), 1, 1));
        detail.Children.Add(Text("HOME、截图、C、GL / GR 仅用于输入检测，暂未映射到 Xbox 输出。", 11, false, "78858B"));
        page.Children.Add(Card(detail, 24));
        var deadzone = new StackPanel { Spacing = 10, Children = { Text("摇杆死区", 17, true), Text("轻微中心偏移由死区过滤；可以到手柄测试页查看处理前后的差别。", 12, false, "78858B") } };
        var value = Text($"{settings.Deadzone:P0}", 14, true, "187B78");
        var slider = new Slider { Minimum = 0, Maximum = 30, StepFrequency = 1, Value = settings.Deadzone * 100, Header = "中心死区（0–30%）" };
        slider.ValueChanged += (_, args) => { settings.Deadzone = args.NewValue / 100; value.Text = $"{settings.Deadzone:P0}"; SaveSettings(); };
        deadzone.Children.Add(value); deadzone.Children.Add(slider); page.Children.Add(Card(deadzone, 24));
    }

    private void BuildSettingsPage()
    {
        title.Text = "设置与诊断"; subtitle.Text = "保持简单，也保留需要时的排查工具。";
        var behavior = new StackPanel { Spacing = 18, Children = { Text("运行方式", 17, true) } };
        var trayToggle = new ToggleSwitch { Header = "关闭窗口后留在托盘", OnContent = "连接继续运行", OffContent = "断开并退出", IsOn = settings.MinimizeToTray };
        trayToggle.Toggled += (_, _) => { settings.MinimizeToTray = trayToggle.IsOn; SaveSettings(); };
        var auto = new ToggleSwitch { Header = "启动应用时开始等待手柄", OnContent = "等待 SYNC 广播", OffContent = "手动点击连接", IsOn = settings.AutoStartConnection };
        auto.Toggled += (_, _) => { settings.AutoStartConnection = auto.IsOn; SaveSettings(); };
        behavior.Children.Add(trayToggle); behavior.Children.Add(auto);
        var target = new TextBox { Header = "指定手柄蓝牙地址", Text = settings.LastControllerAddress, PlaceholderText = "留空时连接首个处于 SYNC 的 Pro 手柄" };
        var apply = new Button { Content = "保存手柄地址" };
        apply.Click += async (_, _) => {
            var text = target.Text.Trim().ToUpperInvariant();
            if (text.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(text, "^([0-9A-F]{2}:){5}[0-9A-F]{2}$")) {
                await ShowInfoAsync("地址格式不正确", "请使用 AA:BB:CC:DD:EE:FF 格式，或留空以发现新手柄。"); return;
            }
            settings.LastControllerAddress = text; SaveSettings();
            await ShowInfoAsync("已保存", "下一次连接将使用这个手柄地址。");
        };
        behavior.Children.Add(target); behavior.Children.Add(apply); page.Children.Add(Card(behavior, 24));
        var diagnostics = new StackPanel { Spacing = 14, Children = { Text("连接诊断", 17, true) } };
        var result = Text("可检查蓝牙 LE 和 ViGEmBus 驱动。", 12, false, "78858B");
        var check = new Button { Content = "检查运行环境" };
        check.Click += async (_, _) => {
            check.IsEnabled = false;
            try {
                var bluetooth = await ControllerService.CheckBluetoothAsync();
                var driver = await Task.Run(VirtualGamepad.CheckDriver);
                result.Text = bluetooth + " · " + driver;
                AppLog.Write("info", result.Text);
            } catch (Exception error) { result.Text = error.Message; AppLog.Write("error", error.Message); }
            finally { check.IsEnabled = true; }
        };
        var logs = new Button { Content = "打开日志文件夹" };
        logs.Click += (_, _) => { Directory.CreateDirectory(AppSettings.DataDirectory); Process.Start(new ProcessStartInfo(AppSettings.DataDirectory) { UseShellExecute = true }); };
        var copy = new Button { Content = "复制诊断信息" };
        copy.Click += (_, _) => {
            var package = new DataPackage();
            package.SetText($"NS2 Pro v{AppVersion}\nWindows: {Environment.OSVersion}\n状态: {controller.Info}\n预设: {settings.Layout}\n死区: {settings.Deadzone:P0}\n\n" + string.Join(Environment.NewLine, logLines));
            Clipboard.SetContent(package); result.Text = "诊断信息已复制";
        };
        diagnostics.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { check, logs, copy } });
        diagnostics.Children.Add(result); logBox.Text = string.Join(Environment.NewLine, logLines); diagnostics.Children.Add(logBox);
        page.Children.Add(Card(diagnostics, 24));
        var exit = new Button { Content = "断开连接并退出应用", HorizontalAlignment = HorizontalAlignment.Left };
        exit.Click += async (_, _) => await ExitAsync(); page.Children.Add(exit);
        page.Children.Add(Text($"NS2 Pro {AppVersion} · WinUI 3 / .NET 10\n使用 Windows 蓝牙 API 与 ViGEmBus。当前支持按键、摇杆与电量；震动、陀螺仪和耳机音频暂未接入。", 12, false, "78858B"));
    }

    private async Task ToggleConnectionAsync()
    {
        try {
            if (controller.Info.Phase is ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting or ConnectionPhase.Connected)
                await controller.StopAsync();
            else await controller.StartAsync();
        } catch (Exception error) { await ShowInfoAsync("连接操作未完成", error.Message); }
    }
    private void UpdateStatus(ConnectionInfo info)
    {
        if (pageFailed || !cachedPages.ContainsKey(selectedPage)) return;
        UpdateMappingLabels();
        badge.Text = info.Phase switch { ConnectionPhase.Connected => "● 已连接", ConnectionPhase.Searching => "● 等待 SYNC", ConnectionPhase.Connecting => "● 连接中", ConnectionPhase.Error => "● 需要处理", _ => "○ 未连接" };
        badge.Foreground = Brush(info.Phase == ConnectionPhase.Connected ? "187B78" : info.Phase == ConnectionPhase.Error ? "AC6635" : "78858B");
        statusText.Text = info.Message;
        addressText.Text = info.Address is null ? "蓝牙 LE · 保留 NS2 配对" : info.Address + " · 蓝牙 LE";
        connectButton.Content = info.Phase switch { ConnectionPhase.Connected => "断开 PC 连接", ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting => "取消连接", ConnectionPhase.Stopping => "正在断开…", _ => "连接手柄" };
        connectButton.IsEnabled = info.Phase != ConnectionPhase.Stopping;
        returnButton.IsEnabled = info.Phase is ConnectionPhase.Connected or ConnectionPhase.Connecting or ConnectionPhase.Searching;
        hzText.Text = info.Phase == ConnectionPhase.Connected ? $"{info.Hertz:F0} Hz" : "—";
        var outputState = info.Phase switch {
            ConnectionPhase.Connected => "游戏输出已就绪",
            ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting => "游戏输出等待连接",
            ConnectionPhase.Stopping => "正在释放游戏输出",
            ConnectionPhase.Error => "游戏输出未就绪",
            _ => "连接后启用游戏输出"
        };
        live.outputStatus.Text = outputState + " · Xbox 360 / XInput";
        live.outputDot.Background = Brush(info.Phase == ConnectionPhase.Connected ? "228F85" : "A4B0B3");
    }
    private void UpdateLiveInput()
    {
        if (pageFailed || !cachedPages.ContainsKey(selectedPage)) return;
        var state = controller.Input;
        if (selectedPage is "overview" or "test") controllerView.Update(state);
        batteryText.Text = state is null ? "—" : $"{state.BatteryPercent}%";
        live.batteryIcon.Update(state?.BatteryPercent);
        inputText.Text = state is null ? "尚未接收输入 · 连接后自动开始检测" : $"● 正在接收 · {controller.Info.Hertz:F1} Hz · {controller.Info.Reports:N0} 份报告";
        var left = state?.UnfilteredLeft ?? new(0, 0); var right = state?.UnfilteredRight ?? new(0, 0);
        var leftFiltered = state?.Left ?? new(0, 0); var rightFiltered = state?.Right ?? new(0, 0);
        leftPlot.Update(left, leftFiltered, settings.Deadzone); rightPlot.Update(right, rightFiltered, settings.Deadzone);
        leftValues.Text = $"X  {left.X:+0.000;-0.000;+0.000}    Y  {left.Y:+0.000;-0.000;+0.000}";
        rightValues.Text = $"X  {right.X:+0.000;-0.000;+0.000}    Y  {right.Y:+0.000;-0.000;+0.000}";
        leftOutput.Text = $"输出  {leftFiltered.X:F3} / {leftFiltered.Y:F3}"; rightOutput.Text = $"输出  {rightFiltered.X:F3} / {rightFiltered.Y:F3}";
        leftOffset.Text = $"中心偏移  {Math.Sqrt(left.X * left.X + left.Y * left.Y) * 100:F1}%";
        rightOffset.Text = $"中心偏移  {Math.Sqrt(right.X * right.X + right.Y * right.Y) * 100:F1}%";
        leftTrigger.Value = state?.Xbox.LeftTrigger ?? 0; rightTrigger.Value = state?.Xbox.RightTrigger ?? 0;
        xboxButtons.Text = state is null ? "—" : XboxLabels(state.Xbox);
    }
    private static string XboxLabels(XboxReport input)
    {
        var pressed = new List<string>();
        foreach (var (mask, label) in new (ushort, string)[] { (0x1000,"A"),(0x2000,"B"),(0x4000,"X"),(0x8000,"Y"),(0x100,"LB"),(0x200,"RB"),(0x10,"Start"),(0x20,"Back"),(0x40,"LS"),(0x80,"RS"),(1,"↑"),(2,"↓"),(4,"←"),(8,"→") })
            if ((input.Buttons & mask) != 0) pressed.Add(label);
        if (input.LeftTrigger > 0) pressed.Add("LT"); if (input.RightTrigger > 0) pressed.Add("RT");
        return pressed.Count == 0 ? "未按下按键" : string.Join("  ·  ", pressed);
    }
    private void ChangeLayout(ButtonLayout layout) { settings.Layout = layout; SaveSettings(); AppLog.Write("info", "映射预设：" + (layout == ButtonLayout.Labels ? "Nintendo 布局 · 字母对应" : "Xbox 布局 · 位置对应")); }
    private void SaveSettings() { try { settings.Save(); } catch (Exception error) { AppLog.Write("error", "保存设置失败：" + error.Message); } }
    private void SelectPage(string tag) => navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().First(x => (string)x.Tag == tag);
    private void LogWritten(string line) => DispatcherQueue.TryEnqueue(() => { logLines.Enqueue(line); while (logLines.Count > 80) logLines.Dequeue(); if (selectedPage == "settings") logBox.Text = string.Join(Environment.NewLine, logLines); });
    private async Task ShowInfoAsync(string heading, string description) => await new ContentDialog { Title = heading, Content = description, CloseButtonText = "知道了", XamlRoot = ((FrameworkElement)Content).XamlRoot }.ShowAsync();
    private async Task ExitAsync()
    {
        if (exiting) return; exiting = true;
        await controller.StopAsync(); timer.Stop(); tray?.Dispose(); exitAllowed = true; Close();
    }

    private Border LayoutCard(ButtonLayout layout)
    {
        bool nintendo = layout == ButtonLayout.Labels;
        var choice = new RadioButton { Content = nintendo ? "Nintendo 布局" : "Xbox 布局", GroupName = "Layout", IsChecked = settings.Layout == layout, FontSize = 18 };
        AutomationProperties.SetName(choice, nintendo ? "Nintendo 布局，字母对应，A 对应 A" : "Xbox 布局，位置对应，A 对应 B");
        var content = new StackPanel { Spacing = 15, Children = { choice,
            Text(nintendo ? "字母对应" : "位置对应", 12, true, "187B78"),
            Text(nintendo ? "游戏提示 A，对应手柄 A。" : "保持 Xbox 的 ABXY 按键位置。", 13, false, "78858B") } };
        var diagrams = new Grid { ColumnSpacing = 6 };
        diagrams.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        diagrams.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        diagrams.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        diagrams.Children.Add(ButtonDiamond(false, "手柄"));
        var arrow = Text("→", 22, false, "8A969B"); arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow, 1); diagrams.Children.Add(arrow);
        var output = ButtonDiamond(!nintendo, "游戏输出"); Grid.SetColumn(output, 2); diagrams.Children.Add(output);
        content.Children.Add(diagrams);
        content.Children.Add(Text(nintendo ? "A → A   B → B   X → X   Y → Y" : "A → B   B → A   X → Y   Y → X", 12, true, "516168"));
        var card = Card(content, 22);
        card.BorderBrush = Brush(settings.Layout == layout ? "187B78" : "E3E9E9");
        card.Background = Brush(settings.Layout == layout ? "F5FAF8" : "FFFFFF");
        choice.Checked += (_, _) => { card.BorderBrush = Brush("187B78"); card.Background = Brush("F5FAF8"); ChangeLayout(layout); };
        choice.Unchecked += (_, _) => { card.BorderBrush = Brush("E3E9E9"); card.Background = Brush("FFFFFF"); };
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
            var text = Text(letters[i], 13, true, "187B78"); text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            var key = new Border { Width = 31, Height = 31, CornerRadius = new(16), Background = Brush("EAF3F1"), Child = text };
            Grid.SetRow(key, coordinates[i].Item1); Grid.SetColumn(key, coordinates[i].Item2); grid.Children.Add(key);
        }
        return new StackPanel { Spacing = 10, Children = { grid, new TextBlock { Text = label, FontSize = 11, Foreground = Brush("78858B"), HorizontalAlignment = HorizontalAlignment.Center } } };
    }

    private static Border MappingTile(string label, FrameworkElement source, FrameworkElement output)
    {
        var bridge = new Grid { ColumnSpacing = 12, Margin = new(0, 7, 0, 0) };
        bridge.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        bridge.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bridge.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        source.HorizontalAlignment = output.HorizontalAlignment = HorizontalAlignment.Center;
        bridge.Children.Add(source);
        var arrow = new FontIcon { Glyph = "\uE72A", FontFamily = new("Segoe Fluent Icons"), FontSize = 17, Foreground = Brush("9AA9AD"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1); bridge.Children.Add(arrow); Grid.SetColumn(output, 2); bridge.Children.Add(output);
        return new Border { Background = Brush("F5F8F7"), CornerRadius = new(12), Padding = new(16, 13, 16, 15),
            Child = new StackPanel { Spacing = 4, Children = { Text(label, 12, true, "667C82"), bridge } } };
    }

    private static StackPanel Keycaps(bool output, bool round, params string[] labels)
    {
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var label in labels) {
            var text = Text(label, label.Length > 3 ? 11 : 13, true, output ? "FFFFFF" : "3C565E");
            text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
            var key = new Border { Width = label.Length > 3 ? 54 : 40, Height = 36, CornerRadius = new(round ? 18 : 8),
                Background = Brush(output ? "247F79" : "FFFFFF"), BorderBrush = Brush(output ? "247F79" : "D8E2E1"), BorderThickness = new(1), Child = text };
            pair.Children.Add(key);
        }
        return pair;
    }

    private static Viewbox DirectionPad(bool output)
    {
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas { Width = 44, Height = 44 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M 16 2 Q 14 2 14 4 L 14 14 L 4 14 Q 2 14 2 16 L 2 28 Q 2 30 4 30 L 14 30 L 14 40 Q 14 42 16 42 L 28 42 Q 30 42 30 40 L 30 30 L 40 30 Q 42 30 42 28 L 42 16 Q 42 14 40 14 L 30 14 L 30 4 Q 30 2 28 2 Z'/>");
        path.Fill = Brush(output ? "247F79" : "FFFFFF"); path.Stroke = Brush(output ? "247F79" : "D8E2E1"); path.StrokeThickness = 1;
        canvas.Children.Add(path);
        var center = Text("+", 19, true, output ? "D5EDE8" : "667C82"); center.Width = center.Height = 44; center.TextAlignment = TextAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center; center.Padding = new(0, 7, 0, 0); canvas.Children.Add(center);
        return new Viewbox { Width = 44, Height = 40, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private void UpdateMappingLabels()
    {
        var labels = settings.Layout == ButtonLayout.Labels ? new[] { "A → A", "B → B", "X → X", "Y → Y" } : new[] { "A → B", "B → A", "X → Y", "Y → X" };
        foreach (var cached in cachedPages.Values) for (int i = 0; i < cached.Live.MappingLabels.Count; i++) cached.Live.MappingLabels[i].Text = labels[i];
    }
    private StackPanel MappingPreview()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var maps = settings.Layout == ButtonLayout.Labels ? new[] { "A → A", "B → B", "X → X", "Y → Y" } : new[] { "A → B", "B → A", "X → Y", "Y → X" };
        foreach (var text in maps) {
            var label = Text(text, 13, true, "187B78"); live.MappingLabels.Add(label);
            row.Children.Add(new Border { Padding = new(16, 9, 16, 9), CornerRadius = new(9), Background = Brush("F0F6F5"), Child = label });
        }
        return row;
    }
    private static Border Metric(string label, FrameworkElement value) => Card(new StackPanel { Spacing = 10, Children = { Text(label, 12, false, "78858B"), value } }, 20);
    private static Border StickCard(string label, StickPlot plot, TextBlock values, TextBlock output, TextBlock offset) => Card(new StackPanel { Spacing = 8, Children = { Text(label, 15, true), plot, values, output, offset } }, 20);
    private static Border TriggerCard(string label, ProgressBar bar) => Card(new StackPanel { Spacing = 12, Children = { Text(label, 13, true), bar } }, 20);
    private static Grid Columns(FrameworkElement first, FrameworkElement second, double a, double b, FrameworkElement? third = null)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new() { Width = new(a, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(b, GridUnitType.Star) });
        grid.Children.Add(first); Grid.SetColumn(second, 1); grid.Children.Add(second);
        if (third is not null) { grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.SetColumn(third, 2); grid.Children.Add(third); }
        return grid;
    }
    private static Border Card(UIElement child, double padding) => new() { Background = Brush("FFFFFF"), BorderBrush = Brush("E3E9E9"), BorderThickness = new(1), CornerRadius = new(16), Padding = new(padding), Child = child };
    private static TextBlock Text(string text, double size = 14, bool bold = false, string color = "263B43") => new() {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(color), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
    private static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
}
