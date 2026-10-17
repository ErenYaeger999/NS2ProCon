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

    private UIElement HapticBackendOptions()
    {
        var content = new StackPanel { Spacing = 13 };
        var modes = new ComboBox { Header = "游戏输出模式 · 下次连接生效", HorizontalAlignment = HorizontalAlignment.Stretch };
        modes.Items.Add("Xbox 兼容模式（推荐）"); modes.Items.Add("DualSense 触觉增强（实验）");
        modes.SelectedIndex = (int)settings.OutputMode;
        modes.SelectionChanged += (_, _) => {
            if (modes.SelectedIndex < 0) return;
            settings.OutputMode = (GameOutputMode)modes.SelectedIndex; SaveSettings();
        };
        content.Children.Add(modes);
        content.Children.Add(Text("兼容模式使用现有驱动。实验模式需要单独运行兼容 VPCM V5 的 VIIPER，并安装其要求的签名 USBIP 驱动；本应用不会自动下载安装后端。", 12, false, AppPalette.Muted));
        content.Children.Add(Text("在启动游戏前选好模式并重新连接。原生触觉需要游戏支持，部分游戏还需关闭该游戏的 Steam Input；不支持时请切回 Xbox 兼容模式。NS2 Pro 不会因此获得自适应扳机阻力。", 12, false, AppPalette.Muted));
        var port = new NumberBox { Header = "本机后端端口", Minimum = 1024, Maximum = 65535,
            Value = settings.ViiperPort, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        port.ValueChanged += (_, args) => {
            if (!double.IsFinite(args.NewValue)) return;
            settings.ViiperPort = (int)Math.Clamp(args.NewValue, 1024, 65535); SaveSettings();
        };
        content.Children.Add(port);
        var check = new Button { Content = "检查本地触觉后端" };
        var result = Text("仅连接本机，不采集系统声音。触觉波形会转换为手柄参数，尚待实物与游戏兼容验证。", 12, false, AppPalette.Muted);
        check.Click += async (_, _) => {
            check.IsEnabled = false;
            try { result.Text = await ViiperGamepad.ProbeAsync(settings.ViiperPort) + " · 已找到；连接后还将检查 V5 反馈格式及虚拟设备。"; }
            catch (Exception error) { result.Text = error.Message; }
            finally { check.IsEnabled = true; }
        };
        content.Children.Add(check); content.Children.Add(result);
        return new Expander { Header = "实验性触觉增强", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = content };
    }

    private void UpdateRumbleStatus()
    {
        var status = controller.Haptics;
        foreach (var button in rumbleTests) button.IsEnabled = status.Ready && status.Enabled && settings.RumbleStrength > 0;
        if (rumbleStop is not null) rumbleStop.IsEnabled = status.Ready;
        if (rumbleStatus is not null) rumbleStatus.Text = status.Faulted ? status.Message :
            !settings.RumbleEnabled || settings.RumbleStrength == 0 ? "震动已关闭，可在设置中开启或调整强度" :
            !status.Ready ? status.Message : status.Testing ? "正在测试 · 自动停止，也可随时点「立即停止」" : "震动已就绪 · 可选择效果，或在游戏中体验";
        if (rumbleTelemetry is not null) {
            rumbleTelemetry.Text = status.Ready ? $"游戏反馈 {status.Feedback:N0} 次 · 低频 {status.Low / 255d:P0} / 高频 {status.High / 255d:P0}" : "";
            rumbleTelemetry.Visibility = status.Ready ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
