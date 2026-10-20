using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NS2Pro.App.Controls;
using NS2Pro.App.Services;
using NS2Pro.Core;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private Border? motionSettings;
    private ToggleSwitch? motionToggle;
    private TextBlock? motionSettingHint, motionMessage, motionMetrics;
    private Button? motionCalibrate;
    private ProgressBar? motionProgress;
    private MotionView? motionView;
    private HyperlinkButton? motionSettingsLink;
    private Border? motionFirstUse;
    private TextBlock? motionNextStep;

    private void OpenMotionTest()
    {
        SelectPage("test");
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => (motionFirstUse?.Parent as FrameworkElement)?.StartBringIntoView());
    }

    private UIElement MotionSettingsCard()
    {
        var content = new StackPanel { Spacing = 12, Children = { Text("体感输入", 17, true) } };
        motionToggle = new ToggleSwitch { Header = "向 DualSense 提供六轴体感", IsOn = settings.DualSenseMotionEnabled,
            OnContent = "由游戏或 Steam 使用", OffContent = "关闭体感输入" };
        motionToggle.Toggled += (_, _) => {
            if (controller.Info.IsActive) { motionToggle.IsOn = settings.DualSenseMotionEnabled; return; }
            settings.DualSenseMotionEnabled = motionToggle.IsOn; SaveSettings(); UpdateMotionUi();
        };
        content.Children.Add(motionToggle);
        content.Children.Add(Text("让支持的游戏或 Steam 识别手柄的转动。连接器不自动开启体感瞄准；灵敏度和启用键由游戏／Steam 设置。", 12, false, AppPalette.Muted));
        content.Children.Add(Text("Steam Input 接管控制器后，可能影响游戏原生触觉，请按游戏选择。", 12, false, AppPalette.Muted));
        motionSettingHint = Text("首次使用建议到「手柄测试」静置校准。", 12, false, AppPalette.Muted);
        content.Children.Add(motionSettingHint);
        var test = new HyperlinkButton { Content = "打开体感测试", Padding = new(0) };
        test.Click += (_, _) => OpenMotionTest();
        content.Children.Add(test);
        motionSettings = Card(content, 24); return motionSettings;
    }

    private UIElement MotionTestCard()
    {
        var content = new StackPanel { Spacing = 14 };
        var heading = new Grid { ColumnSpacing = 14 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(Text("体感测试", 17, true));
        motionCalibrate = new Button { Content = "校准体感" };
        motionCalibrate.Click += (_, _) => { controller.CalibrateMotion(); UpdateMotionUi(); };
        Grid.SetColumn(motionCalibrate, 1); heading.Children.Add(motionCalibrate); content.Children.Add(heading);
        content.Children.Add(Text("DualSense 六轴输入 · 指示转动速度，静置时应接近零。", 12, false, AppPalette.Muted));
        var steps = new StackPanel { Spacing = 10, Children = { Text("首次使用", 13, true) } };
        foreach (var (number, message) in new[] { ("1", "用增强模式连接，转动手柄确认三轴有响应。"),
            ("2", "放稳桌面，点「校准体感」并静置约 2 秒。期间暂停震动，校准结果会保存。"),
            ("3", "在支持的游戏或 Steam 控制器布局中开启体感瞄准，再调整灵敏度与启用键。") }) {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = new(22) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new(11), Background = Brush("EEEEEE"),
                VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = number, FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var copy = Text(message, 12, false, AppPalette.Muted); Grid.SetColumn(copy, 1); row.Children.Add(copy); steps.Children.Add(row);
        }
        motionFirstUse = new Border { Background = Brush(AppPalette.Page), CornerRadius = new(10), Padding = new(16), Child = steps };
        content.Children.Add(motionFirstUse);
        motionView = new(); content.Children.Add(motionView);
        motionProgress = new ProgressBar { Maximum = 1, Height = 4, Visibility = Visibility.Collapsed };
        motionMessage = Text("使用增强模式连接后可检测六轴体感。", 12, false, AppPalette.Muted);
        motionMetrics = Text("", 11, false, AppPalette.Muted);
        ToolTipService.SetToolTip(motionMetrics, "统计真实有效的体感样本，不是虚拟手柄发送频率，也不代表端到端延迟。");
        motionNextStep = Text("", 12, false, AppPalette.Muted);
        motionSettingsLink = new HyperlinkButton { Content = "设置游戏模式", Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        motionSettingsLink.Click += (_, _) => OpenEnhancementSettings();
        content.Children.Add(motionProgress); content.Children.Add(motionMessage); content.Children.Add(motionMetrics); content.Children.Add(motionNextStep); content.Children.Add(motionSettingsLink);
        return Card(content, 22);
    }

    private void UpdateMotionUi()
    {
        bool enhanced = CurrentOutputMode == GameOutputMode.DualSenseExperimental;
        if (motionSettings is not null) motionSettings.Visibility = wantsEnhanced ? Visibility.Visible : Visibility.Collapsed;
        if (motionToggle is not null) motionToggle.IsEnabled = !controller.Info.IsActive && !AppSettings.IsPreview;
        if (motionSettingHint is not null) motionSettingHint.Text = controller.Info.IsActive ?
            "请先断开 PC 连接，再调整体感开关。" : "首次使用建议到「手柄测试」静置校准。";
        if (motionMessage is null) return;
        var state = enhanced && settings.DualSenseMotionEnabled ? controller.Motion : null;
        motionMessage.Text = !enhanced ? "体感通过 DualSense 输出，请在设置中选择原生触觉增强后连接。" :
            !settings.DualSenseMotionEnabled ? "体感输入已关闭，可在设置中开启后重新连接。" :
            !controller.Info.HasLink ? "用增强模式连接手柄后，转动手柄即可检测。" : state?.Message ?? "正在准备体感输入…";
        motionView!.Update(state?.State);
        motionCalibrate!.IsEnabled = controller.Info.Phase == ConnectionPhase.Connected && state is { Receiving: true, Calibrating: false };
        motionCalibrate.Content = state?.Calibrating == true ? "正在校准…" : "校准体感";
        motionProgress!.Visibility = state?.Calibrating == true ? Visibility.Visible : Visibility.Collapsed;
        motionProgress.Value = state?.Progress ?? 0;
        motionMetrics!.Text = state is { Receiving: true } ?
            $"体感采样 {state.Hertz:F0} Hz · {(state.Calibrated ? "已保存校准" : "尚未校准")}" : "";
        motionMetrics.Visibility = state?.Receiving == true ? Visibility.Visible : Visibility.Collapsed;
        motionFirstUse!.Visibility = enhanced && settings.DualSenseMotionEnabled && state?.Calibrated != true ? Visibility.Visible : Visibility.Collapsed;
        motionNextStep!.Text = state?.Calibrating == true ? "请保持静止；有晃动会重新等待。" :
            state is { Receiving: true, Calibrated: true } ? "已准备好传递体感。请在游戏／Steam 中开启体感瞄准；游戏不支持时不会自动生效。" :
            state is { Receiving: true } ? "三轴有响应后，可先静置校准，再到游戏／Steam 开启体感。" : "";
        motionNextStep.Visibility = motionNextStep.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        motionSettingsLink!.Visibility = !enhanced || !settings.DualSenseMotionEnabled ? Visibility.Visible : Visibility.Collapsed;
    }
}
