using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NS2Pro.Core;

namespace NS2Pro.App.Controls;

/// <summary>Angular-velocity indicators, not an integrated/fabricated absolute controller pose.</summary>
public sealed class MotionView : UserControl
{
    private readonly Ellipse[] dots = new Ellipse[3];
    private readonly Line[] fills = new Line[3];
    private readonly Line[] baselines = new Line[3], centers = new Line[3];
    private readonly Canvas[] tracks = new Canvas[3];
    private readonly TextBlock[] values = new TextBlock[3];
    private MotionState? current;
    public MotionView()
    {
        var rows = new StackPanel { Spacing = 16 };
        var accent = new SolidColorBrush(ColorHelper.FromArgb(255, 230, 0, 18));
        var muted = new SolidColorBrush(ColorHelper.FromArgb(255, 112, 112, 112));
        var track = new SolidColorBrush(ColorHelper.FromArgb(255, 233, 233, 233));
        int i = 0;
        foreach (string name in new[] { "俯仰", "左右转动", "侧倾" }) {
            var row = new Grid { ColumnSpacing = 12, Height = 32 };
            row.ColumnDefinitions.Add(new() { Width = new(64) });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new(88) });
            row.Children.Add(new TextBlock { Text = name, FontSize = 14, Foreground = muted, VerticalAlignment = VerticalAlignment.Center });
            var canvas = new Canvas { Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
            tracks[i] = canvas;
            baselines[i] = new() { Y1 = 16, Y2 = 16, Stroke = track, StrokeThickness = 4 };
            centers[i] = new() { Y1 = 8, Y2 = 24, Stroke = muted, StrokeThickness = 1 };
            canvas.Children.Add(baselines[i]); canvas.Children.Add(centers[i]);
            fills[i] = new() { Y1 = 16, Y2 = 16, Stroke = accent, StrokeThickness = 4 };
            canvas.Children.Add(fills[i]);
            dots[i] = new() { Width = 10, Height = 10, Fill = accent, Visibility = Visibility.Collapsed };
            Canvas.SetTop(dots[i], 11); canvas.Children.Add(dots[i]);
            values[i] = new() { Text = "—", FontSize = 13, Foreground = muted, FontFamily = new("Consolas"),
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(canvas, 1); row.Children.Add(canvas);
            Grid.SetColumn(values[i], 2); row.Children.Add(values[i]); rows.Children.Add(row);
            canvas.SizeChanged += (_, _) => Update(current);
            i++;
        }
        Content = rows;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "陀螺仪三轴转动速度检测");
    }

    public void Update(MotionState? state)
    {
        current = state;
        double[] axes = [state?.Gyroscope.X ?? 0, state?.Gyroscope.Y ?? 0, state?.Gyroscope.Z ?? 0];
        for (int i = 0; i < 3; i++) {
            double value = double.IsFinite(axes[i]) ? axes[i] : 0;
            double width = Math.Max(0, tracks[i].ActualWidth - 10), center = width / 2 + 5;
            double x = center + Math.Clamp(value / 180, -1, 1) * width / 2;
            baselines[i].X1 = 5; baselines[i].X2 = width + 5;
            centers[i].X1 = centers[i].X2 = center;
            fills[i].X1 = center; fills[i].X2 = x; Canvas.SetLeft(dots[i], x - 5);
            dots[i].Visibility = state is null ? Visibility.Collapsed : Visibility.Visible;
            values[i].Text = state is null ? "—" : $"{value,+7:0.0;-0.0;0.0} °/s";
        }
    }
}
