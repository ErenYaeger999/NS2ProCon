using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NS2Pro.App.Services;
using NS2Pro.Core;
using Windows.System;

namespace NS2Pro.App.Controls;

public sealed class ConnectionGuideView : UserControl
{
    private readonly AppSettings settings;
    private readonly ControllerService controller;
    private EnvironmentReport? report;
    private readonly StackPanel checks = new() { Spacing = 12, MinHeight = 140 };
    private readonly TextBlock environmentStatus = Label("正在检查电脑…", 12);
    private readonly TextBlock connectionStatus = Label("环境就绪后，点击开始连接。", 13);
    private readonly TextBlock confirmation = Label("收到手柄输入后，这一步会自动完成。", 13);
    private readonly TextBlock scanHelp = Label("请保持手柄靠近电脑。无需在 Windows 蓝牙页面里手动配对。", 12);
    private readonly Button refresh = new() { Content = "重新检查" };
    private readonly Button driver = new() { Content = "安装手柄驱动", Visibility = Visibility.Collapsed };
    private readonly Button start = new() { Content = "开始连接", Height = 44, IsEnabled = false };
    private readonly Button stop = new() { Content = "取消连接", Height = 44, Visibility = Visibility.Collapsed };
    private readonly Button test = new() { Content = "打开手柄测试" };
    private bool checking, loaded;
    private DateTimeOffset searchStarted;
    private ConnectionPhase previousPhase = (ConnectionPhase)(-1);
    public event Action? InitialCheckCompleted;

    public ConnectionGuideView(AppSettings settings, ControllerService controller, Action openTest, Action openOverview)
    {
        this.settings = settings; this.controller = controller;
        var content = new StackPanel { Spacing = 20 };
        var env = new StackPanel { Spacing = 16, Children = { Heading("1", "检查这台电脑"), checks, environmentStatus } };
        var bluetooth = new Button { Content = "打开蓝牙设置" };
        bluetooth.Click += async (_, _) => { try { await Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth")); } catch (Exception e) { environmentStatus.Text = e.Message; } };
        var tools = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 3, ItemWidth = 160, ItemHeight = 44 };
        foreach (var button in new[] { refresh, bluetooth, driver }) { button.Margin = new(0, 0, 8, 8); tools.Children.Add(button); }
        env.Children.Add(tools);
        content.Children.Add(Card(env));

        var steps = new StackPanel { Spacing = 14, Children = {
            Heading("2", "用 SYNC 连接 PC"),
            Label("先拔掉 USB 线，把手柄放在电脑附近。", 14),
            Label("点击下方「开始连接」，再长按手柄顶部的 SYNC 小圆键，直到指示灯循环闪烁。", 14),
            new Border { Background = Brush(AppPalette.Soft), CornerRadius = new(10), Padding = new(15), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
                new Border { Width = 36, Height = 36, CornerRadius = new(18), Background = Brush(AppPalette.Accent), Child = new FontIcon { Glyph = "\uE895", FontSize = 18, Foreground = Brush("FFFFFF") } },
                new StackPanel { Spacing = 2, Children = { Label("长按顶部 SYNC", 14, true), Label("连接 PC 请用 SYNC；HOME 会唤醒 NS2。", 12) } }
            } } }, connectionStatus
        } };
        start.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        steps.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { start, stop } });
        steps.Children.Add(scanHelp);
        var tips = new StackPanel { Spacing = 9, Children = {
            Label("• 请确认 USB 已拔掉，并长按 SYNC，让指示灯循环闪烁。", 12),
            Label("• 关闭其他正在接管这只手柄的连接工具，再重新尝试。", 12),
            Label("• 不要按 HOME 来连接 PC；使用结束后断开 PC，再按 HOME 回到 NS2。", 12)
        } };
        var discover = new Button { Content = "改为发现新手柄" };
        discover.Click += async (_, _) => {
            discover.IsEnabled = false;
            try {
                await controller.StopAsync(); settings.LastControllerAddress = ""; settings.Save();
                scanHelp.Text = "已清除指定地址。现在点击开始连接，再长按需要连接的手柄 SYNC。";
            } catch (Exception error) { scanHelp.Text = error.Message; }
            finally { discover.IsEnabled = true; }
        };
        tips.Children.Add(discover);
        steps.Children.Add(new Expander { Header = "还没找到手柄？", HorizontalAlignment = HorizontalAlignment.Stretch, Content = tips });
        content.Children.Add(Card(steps));
        test.Click += (_, _) => openTest();
        var overview = new Button { Content = "进入总览" }; overview.Click += (_, _) => openOverview();
        content.Children.Add(Card(new StackPanel { Spacing = 14, Children = {
            Heading("3", "确认按键和摇杆"), confirmation,
            Label("连接成功后，按几下 A / B 并转动双摇杆。测试页会实时显示输入，游戏会识别为 Xbox 360 手柄。", 13),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { test, overview } }
        } }));
        Content = content;
        refresh.Click += async (_, _) => await RefreshEnvironmentAsync();
        driver.Click += async (_, _) => {
            driver.IsEnabled = false;
            try { environmentStatus.Text = await PrerequisiteService.InstallDriverAsync(); }
            catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223) { environmentStatus.Text = "已取消安装。需要游戏输出时，可再次安装驱动。"; }
            catch (Exception error) { environmentStatus.Text = error is FileNotFoundException or DirectoryNotFoundException ? "未找到随包驱动，请重新运行完整安装包。" : error.Message; }
            finally { driver.IsEnabled = true; }
        };
        start.Click += async (_, _) => {
            await RefreshEnvironmentAsync();
            if (report?.Ready != true) return;
            try { await controller.StartAsync(); }
            catch (Exception error) { connectionStatus.Text = error.Message; }
        };
        stop.Click += async (_, _) => { try { await controller.StopAsync(); } catch (Exception error) { connectionStatus.Text = error.Message; } };
        Loaded += async (_, _) => { if (loaded) return; loaded = true; await RefreshEnvironmentAsync(); InitialCheckCompleted?.Invoke(); };
    }

    public async Task RefreshEnvironmentAsync()
    {
        if (checking) return;
        checking = true; refresh.IsEnabled = start.IsEnabled = false; environmentStatus.Text = "正在检查电脑…";
        try {
            report = await PrerequisiteService.CheckAsync();
            checks.Children.Clear();
            foreach (var item in report.Items) {
                checks.Children.Add(new StackPanel { Spacing = 3, Children = {
                    Label((item.Ready ? "✓  " : "○  ") + item.Name, 13, true, item.Ready ? AppPalette.AccentText : "A86C3E"),
                    Label(item.Detail, 12)
                } });
            }
            environmentStatus.Text = report.Ready ? "环境已就绪，可以连接手柄。" : "请先处理上方未通过的项目，再点击重新检查。";
            driver.Visibility = report.Driver.Ready ? Visibility.Collapsed : Visibility.Visible;
        } catch (Exception error) { report = null; environmentStatus.Text = "检查未完成：" + error.Message; }
        finally { checking = false; refresh.IsEnabled = true; Update(controller.Info, controller.Input); }
    }

    public void Update(ConnectionInfo info, ControllerInput? input)
    {
        bool active = info.IsActive;
        start.IsEnabled = !checking && report?.Ready == true && !active;
        start.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        start.Content = info.Phase == ConnectionPhase.Error ? "重试连接" : "开始连接";
        stop.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        stop.IsEnabled = info.Phase != ConnectionPhase.Stopping;
        stop.Content = info.ActionText;
        connectionStatus.Text = info.Phase == ConnectionPhase.Idle ? "环境就绪后，点击开始连接。" : info.Message;
        confirmation.Text = input is not null ? "✓ 已收到真实输入，可以开始游戏或检查按键。" : settings.HasCompletedFirstConnection ? "✓ 已完成过首次连接。可重新连接手柄，或打开测试页。" : "收到手柄输入后，这一步会自动完成。";
        if (info.Phase == ConnectionPhase.Searching && previousPhase != ConnectionPhase.Searching) {
            searchStarted = DateTimeOffset.Now;
            scanHelp.Text = "请保持手柄靠近电脑。无需在 Windows 蓝牙页面里手动配对。";
        }
        if (info.Phase == ConnectionPhase.Searching && DateTimeOffset.Now - searchStarted > TimeSpan.FromSeconds(25))
            scanHelp.Text = "仍在等待 SYNC。确认指示灯循环闪烁；更换手柄时可展开下方提示，改为发现新手柄。";
        previousPhase = info.Phase;
    }

    private static StackPanel Heading(string number, string title) => new() { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
        new Border { Width = 28, Height = 28, CornerRadius = new(14), Background = Brush(AppPalette.SoftStrong), Child = new TextBlock { Text = number, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(AppPalette.AccentText), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
        new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(AppPalette.Text), VerticalAlignment = VerticalAlignment.Center }
    } };
    private static Border Card(UIElement child) => new() { Background = Brush("FFFFFF"), BorderBrush = Brush(AppPalette.Border), BorderThickness = new(1), CornerRadius = new(16), Padding = new(22), Child = child };
    private static TextBlock Label(string text, double size, bool bold = false, string color = AppPalette.Muted) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(color), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
    private static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
}
