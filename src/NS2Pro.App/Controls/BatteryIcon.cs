using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;

namespace NS2Pro.App.Controls;

public sealed class BatteryIcon : UserControl
{
    private readonly Border fill;
    private readonly TextBlock unknown;
    private readonly Microsoft.UI.Xaml.Shapes.Path lightning;
    private (int Level, bool Charging, bool ExternalPower) previous = (int.MinValue, false, false);

    public BatteryIcon()
    {
        Width = 44; Height = 24;
        var root = new Grid();
        root.Children.Add(new Border { Width = 3, Height = 10, CornerRadius = new(1),
            Background = Brush("858585"), HorizontalAlignment = HorizontalAlignment.Right });
        var interior = new Grid();
        fill = new Border { Width = 0, Height = 14, CornerRadius = new(2), HorizontalAlignment = HorizontalAlignment.Left };
        unknown = new TextBlock { Text = "–", FontSize = 12, Foreground = Brush("767676"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        interior.Children.Add(fill); interior.Children.Add(unknown);
        lightning = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M 9 0 L 2 10 H 7 L 4 18 L 14 7 H 9 L 12 0 Z'/>");
        lightning.Fill = Brush("26352B"); lightning.Stroke = Brush("FFFFFF"); lightning.StrokeThickness = 1.5;
        lightning.HorizontalAlignment = HorizontalAlignment.Center; lightning.VerticalAlignment = VerticalAlignment.Center;
        lightning.Visibility = Visibility.Collapsed;
        interior.Children.Add(lightning);
        root.Children.Add(new Border { Width = 39, Height = 24, CornerRadius = new(4), BorderThickness = new(1.5),
            BorderBrush = Brush("858585"), Padding = new(3), HorizontalAlignment = HorizontalAlignment.Left, Child = interior });
        Content = root; Update(null);
    }

    public void Update(int? percent, bool isCharging = false, bool hasExternalPower = false)
    {
        int level = percent is null ? -1 : Math.Clamp(percent.Value, 0, 100);
        if (level < 0) isCharging = hasExternalPower = false;
        var current = (level, isCharging, hasExternalPower);
        if (current == previous) return;
        previous = current;
        fill.Width = level < 0 ? 0 : 30 * level / 100.0;
        fill.Background = Brush(level <= 10 ? "D92D20" : level <= 20 ? "B77900" : "16803C");
        unknown.Visibility = level < 0 ? Visibility.Visible : Visibility.Collapsed;
        lightning.Visibility = isCharging ? Visibility.Visible : Visibility.Collapsed;
        var description = level < 0 ? "手柄电量未知" : $"手柄电量 {level}% · {(isCharging ? "正在充电" : hasExternalPower ? "已接电源，未充电" : "电池供电")}";
        AutomationProperties.SetName(this, description);
        ToolTipService.SetToolTip(this, description);
    }

    private static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(255,
        Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
}
