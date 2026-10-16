using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NS2Pro.App.Controls;

public sealed class BatteryIcon : UserControl
{
    private readonly Border fill;
    private readonly TextBlock unknown;
    private int previous = int.MinValue;

    public BatteryIcon()
    {
        Width = 44; Height = 24; IsHitTestVisible = false;
        var root = new Grid();
        root.Children.Add(new Border { Width = 3, Height = 10, CornerRadius = new(1),
            Background = Brush("858585"), HorizontalAlignment = HorizontalAlignment.Right });
        var interior = new Grid();
        fill = new Border { Width = 0, Height = 14, CornerRadius = new(2), HorizontalAlignment = HorizontalAlignment.Left };
        unknown = new TextBlock { Text = "–", FontSize = 12, Foreground = Brush("767676"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        interior.Children.Add(fill); interior.Children.Add(unknown);
        root.Children.Add(new Border { Width = 39, Height = 24, CornerRadius = new(4), BorderThickness = new(1.5),
            BorderBrush = Brush("858585"), Padding = new(3), HorizontalAlignment = HorizontalAlignment.Left, Child = interior });
        Content = root; Update(null);
    }

    public void Update(int? percent)
    {
        int level = percent is null ? -1 : Math.Clamp(percent.Value, 0, 100);
        if (level == previous) return;
        previous = level;
        fill.Width = level < 0 ? 0 : 30 * level / 100.0;
        fill.Background = Brush(level <= 10 ? "B45E4D" : level <= 20 ? "B18643" : AppPalette.Accent);
        unknown.Visibility = level < 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, level < 0 ? "手柄电量未知" : $"手柄电量 {level}%");
    }

    private static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(255,
        Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
}
