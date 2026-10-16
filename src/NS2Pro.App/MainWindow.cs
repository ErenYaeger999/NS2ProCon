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
    private readonly AppSettings settings = AppSettings.Load();
    private readonly ControllerService controller;
    private readonly NavigationView navigation = new();
    private readonly StackPanel page = new() { Spacing = 22 };
    private readonly TextBlock title = Text("连接总览", 28, true);
    private readonly TextBlock subtitle = Text("连接 PC，随时回到主机。", 13, false, "78858B");
    private readonly TextBlock badge = Text("未连接", 12, true, "65777D");
    private readonly TextBlock statusText = Text("准备好连接你的手柄", 14);
    private readonly TextBlock addressText = Text("蓝牙 LE · 保留 NS2 配对", 12, false, "78858B");
    private readonly TextBlock hzText = Text("—", 26, true);
    private readonly TextBlock batteryText = Text("—", 26, true);
    private readonly TextBlock inputText = Text("等待手柄连接", 12, false, "78858B");
    private readonly ControllerView controllerView = new();
    private readonly StickPlot leftPlot = new(), rightPlot = new();
    private readonly TextBlock leftValues = Text("X  +0.000    Y  +0.000", 12);
    private readonly TextBlock rightValues = Text("X  +0.000    Y  +0.000", 12);
    private readonly TextBlock leftOutput = Text("输出  0.000 / 0.000", 11, false, "78858B");
    private readonly TextBlock rightOutput = Text("输出  0.000 / 0.000", 11, false, "78858B");
    private readonly TextBlock leftOffset = Text("中心偏移  0.0%", 11, false, "78858B");
    private readonly TextBlock rightOffset = Text("中心偏移  0.0%", 11, false, "78858B");
    private readonly ProgressBar leftTrigger = new() { Maximum = 255, Height = 6, Value = 0 };
    private readonly ProgressBar rightTrigger = new() { Maximum = 255, Height = 6, Value = 0 };
    private readonly TextBlock xboxButtons = Text("—", 12);
    private readonly TextBox logBox = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 190, MaxHeight = 260, FontFamily = new("Cascadia Mono"), FontSize = 11 };
    private readonly Button connectButton = new() { Content = "连接手柄", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 44 };
    private readonly Button returnButton = new() { Content = "切回 NS2", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 42 };
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
        navigation.PaneFooter = new StackPanel { Margin = new(20, 0, 0, 20), Spacing = 5, Children = {
            Text("NATIVE · WINUI 3", 10, true, "8A969B"), Text("v0.2.0", 11, false, "8A969B") } };
        AddNavigation("连接总览", "overview", "\uE701");
        AddNavigation("手柄测试", "test", "\uE7FC");
        AddNavigation("按键映射", "mapping", "\uE8AB");
        AddNavigation("设置与诊断", "settings", "\uE713");
        navigation.SelectionChanged += (_, args) => {
            if (args.SelectedItem is NavigationViewItem item) { selectedPage = (string)item.Tag; BuildPage(); }
        };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page };
        var content = new Grid { Margin = new(26, 13, 28, 24) };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 5, Margin = new(0, 0, 0, 24), Children = { title, subtitle } };
        content.Children.Add(heading); Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        navigation.Content = content; Grid.SetRow(navigation, 1); root.Children.Add(navigation); Content = root;
        connectButton.Background = Brush("187B78"); connectButton.Foreground = Brush("FFFFFF");
        connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        returnButton.Click += async (_, _) => { await controller.StopAsync(); await ShowInfoAsync("可以回到 NS2 了", "PC 连接与虚拟手柄已断开。现在按手柄 HOME，正常连接 NS2 即可，无需重新配对。"); };
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
        Closed += (_, _) => { timer.Stop(); tray?.Dispose(); AppLog.LineWritten -= LogWritten; };
        try { tray = new(hwnd, () => DispatcherQueue.TryEnqueue(() => { NativeMethods.ShowExisting(hwnd); Activate(); }),
            () => DispatcherQueue.TryEnqueue(async () => await ExitAsync())); }
        catch (Exception error) { AppLog.Write("warn", error.Message); }
        navigation.SelectedItem = navigation.MenuItems[0];
        AppLog.Write("info", "NS2 Pro v0.2.0 已启动 · WinUI 3 · 字母映射为默认预设");
        if (settings.AutoStartConnection) _ = controller.StartAsync();
    }

    private void AddNavigation(string label, string tag, string glyph) => navigation.MenuItems.Add(new NavigationViewItem {
        Content = label, Tag = tag, Icon = new FontIcon { Glyph = glyph, FontFamily = new("Segoe Fluent Icons") }, Margin = new(8, 3, 8, 3) });
    private static StackPanel Brand() => new() { Margin = new(20, 16, 0, 24), Spacing = 6,
        Children = { new Border { Width = 42, Height = 42, CornerRadius = new(13), Background = Brush("187B78"), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new FontIcon { Glyph = "\uE7FC", FontFamily = new("Segoe Fluent Icons"), FontSize = 22, Foreground = Brush("FFFFFF") } },
            Text("NS2 Pro", 22, true), Text("一只手柄，自在切换", 11, false, "78858B") } };

    private void BuildPage()
    {
        page.Children.Clear();
        // These live controls are re-used between pages, so detach them from their old parents first.
        foreach (var control in new FrameworkElement[] { controllerView, badge, statusText, addressText, hzText, batteryText, inputText,
            connectButton, returnButton, leftPlot, rightPlot, leftValues, rightValues, leftOutput, rightOutput, leftOffset, rightOffset,
            leftTrigger, rightTrigger, xboxButtons, logBox }) Detach(control);
        switch (selectedPage) {
            case "test": BuildTestPage(); break;
            case "mapping": BuildMappingPage(); break;
            case "settings": BuildSettingsPage(); break;
            default: BuildOverview(); break;
        }
        UpdateStatus(controller.Info); UpdateLiveInput();
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
        page.Children.Add(Columns(Metric("输入频率", hzText, "份报告 / 秒"), Metric("手柄电量", batteryText, "来自手柄蓝牙报告"), 1, 1,
            Metric("游戏输出", Text("Xbox 360", 24, true), "XInput · Windows 游戏兼容")));
        var preset = new StackPanel { Spacing = 12, Children = { Text("当前映射", 14, true), MappingPreview() } };
        var edit = new Button { Content = "更改预设", HorizontalAlignment = HorizontalAlignment.Right };
        edit.Click += (_, _) => SelectPage("mapping"); preset.Children.Add(edit);
        page.Children.Add(Card(preset, 20));
    }

    private void BuildTestPage()
    {
        title.Text = "手柄测试"; subtitle.Text = "按下按键、转动摇杆，实时检查每一次输入。";
        var device = new StackPanel { Spacing = 8, Children = { controllerView, inputText } };
        var instructions = new StackPanel { Spacing = 12, Children = {
            Text("实时输入", 18, true), badge, statusText, connectButton,
            Text("按键亮起表示已收到输入。GL / GR / C / HOME 也会显示，但不发送到 Xbox 游戏。", 12, false, "78858B"),
            Text("Xbox 当前按键", 12, true), xboxButtons } };
        page.Children.Add(Columns(Card(device, 16), Card(instructions, 20), 1.7, 1));
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
        title.Text = "按键映射"; subtitle.Text = "选一种顺手的方式，切换后立即生效。";
        var labels = new RadioButton { Content = "字母对应", GroupName = "Layout", IsChecked = settings.Layout == ButtonLayout.Labels };
        var positions = new RadioButton { Content = "物理布局", GroupName = "Layout", IsChecked = settings.Layout == ButtonLayout.Positions };
        labels.Checked += (_, _) => ChangeLayout(ButtonLayout.Labels);
        positions.Checked += (_, _) => ChangeLayout(ButtonLayout.Positions);
        page.Children.Add(Columns(
            Card(new StackPanel { Spacing = 12, Children = { labels, Text("看手柄上的字母操作", 18, true), Text("A → A   B → B   X → X   Y → Y", 14, true, "187B78"),
                Text("游戏提示 A，就按手柄 A。默认推荐预设。", 12, false, "78858B") } }, 24),
            Card(new StackPanel { Spacing = 12, Children = { positions, Text("跟随 Xbox 的按键位置", 18, true), Text("A → B   B → A   X → Y   Y → X", 14, true, "187B78"),
                Text("手柄下方按键对应 Xbox A，适合习惯位置操作。", 12, false, "78858B") } }, 24), 1, 1));
        var detail = new StackPanel { Spacing = 18, Children = { Text("通用按键", 17, true), Text("L / R → LB / RB     ZL / ZR → LT / RT\n+ / − → Start / Back     摇杆按下 → LS / RS\n十字键和双摇杆保持方向对应。", 14) } };
        detail.Children.Add(Text("HOME、截图、C、GL / GR 可在手柄测试页检查；当前版本暂未将它们映射为游戏按键。", 12, false, "78858B"));
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
            package.SetText($"NS2 Pro v0.2.0\nWindows: {Environment.OSVersion}\n状态: {controller.Info}\n预设: {settings.Layout}\n死区: {settings.Deadzone:P0}\n\n" + string.Join(Environment.NewLine, logLines));
            Clipboard.SetContent(package); result.Text = "诊断信息已复制";
        };
        diagnostics.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { check, logs, copy } });
        diagnostics.Children.Add(result); logBox.Text = string.Join(Environment.NewLine, logLines); diagnostics.Children.Add(logBox);
        page.Children.Add(Card(diagnostics, 24));
        var exit = new Button { Content = "断开连接并退出应用", HorizontalAlignment = HorizontalAlignment.Left };
        exit.Click += async (_, _) => await ExitAsync(); page.Children.Add(exit);
        page.Children.Add(Text("NS2 Pro 0.2.0 · WinUI 3 / .NET 10\n使用 Windows 蓝牙 API 与 ViGEmBus。当前支持按键、摇杆与电量；震动、陀螺仪和耳机音频暂未接入。", 12, false, "78858B"));
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
        badge.Text = info.Phase switch { ConnectionPhase.Connected => "● 已连接", ConnectionPhase.Searching => "● 等待 SYNC", ConnectionPhase.Connecting => "● 连接中", ConnectionPhase.Error => "● 需要处理", _ => "○ 未连接" };
        badge.Foreground = Brush(info.Phase == ConnectionPhase.Connected ? "187B78" : info.Phase == ConnectionPhase.Error ? "AC6635" : "78858B");
        statusText.Text = info.Message;
        addressText.Text = info.Address is null ? "蓝牙 LE · 保留 NS2 配对" : info.Address + " · 蓝牙 LE";
        connectButton.Content = info.Phase switch { ConnectionPhase.Connected => "断开 PC 连接", ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting => "取消连接", ConnectionPhase.Stopping => "正在断开…", _ => "连接手柄" };
        connectButton.IsEnabled = info.Phase != ConnectionPhase.Stopping;
        returnButton.IsEnabled = info.Phase is ConnectionPhase.Connected or ConnectionPhase.Connecting or ConnectionPhase.Searching;
        hzText.Text = info.Phase == ConnectionPhase.Connected ? $"{info.Hertz:F0} Hz" : "—";
    }
    private void UpdateLiveInput()
    {
        var state = controller.Input;
        controllerView.Update(state);
        batteryText.Text = state is null ? "—" : $"{state.BatteryPercent}%";
        inputText.Text = state is null ? "等待手柄连接" : $"实时输入 · {controller.Info.Hertz:F1} Hz · {controller.Info.Reports:N0} 份报告";
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
    private void ChangeLayout(ButtonLayout layout) { settings.Layout = layout; SaveSettings(); AppLog.Write("info", "映射预设：" + (layout == ButtonLayout.Labels ? "字母对应" : "物理布局")); }
    private void SaveSettings() { try { settings.Save(); } catch (Exception error) { AppLog.Write("error", "保存设置失败：" + error.Message); } }
    private void SelectPage(string tag) => navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().First(x => (string)x.Tag == tag);
    private void LogWritten(string line) => DispatcherQueue.TryEnqueue(() => { logLines.Enqueue(line); while (logLines.Count > 80) logLines.Dequeue(); if (selectedPage == "settings") logBox.Text = string.Join(Environment.NewLine, logLines); });
    private async Task ShowInfoAsync(string heading, string description) => await new ContentDialog { Title = heading, Content = description, CloseButtonText = "知道了", XamlRoot = ((FrameworkElement)Content).XamlRoot }.ShowAsync();
    private async Task ExitAsync()
    {
        if (exiting) return; exiting = true;
        await controller.StopAsync(); timer.Stop(); tray?.Dispose(); exitAllowed = true; Close();
    }

    private StackPanel MappingPreview()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var maps = settings.Layout == ButtonLayout.Labels ? new[] { "A → A", "B → B", "X → X", "Y → Y" } : new[] { "A → B", "B → A", "X → Y", "Y → X" };
        foreach (var text in maps) row.Children.Add(new Border { Padding = new(16, 9, 16, 9), CornerRadius = new(9), Background = Brush("F0F6F5"), Child = Text(text, 13, true, "187B78") });
        return row;
    }
    private static Border Metric(string label, TextBlock value, string detail) => Card(new StackPanel { Spacing = 7, Children = { Text(label, 12, false, "78858B"), value, Text(detail, 11, false, "8A969B") } }, 20);
    private static Border StickCard(string label, StickPlot plot, TextBlock values, TextBlock output, TextBlock offset) => Card(new StackPanel { Spacing = 8, Children = { Text(label, 15, true), plot, values, output, offset } }, 20);
    private static Border TriggerCard(string label, ProgressBar bar) => Card(new StackPanel { Spacing = 12, Children = { Text(label, 13, true), bar } }, 20);
    private static Grid Columns(UIElement first, UIElement second, double a, double b, UIElement? third = null)
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
    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is Border border) border.Child = null;
        else if (element.Parent is ContentControl content) content.Content = null;
    }
}
