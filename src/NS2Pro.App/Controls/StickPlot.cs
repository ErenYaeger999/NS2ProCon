using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NS2Pro.Core;
using Windows.Foundation;

namespace NS2Pro.App.Controls;

public sealed class StickPlot : UserControl
{
    private readonly Ellipse rawDot, outputDot, deadzoneCircle;
    private readonly Polyline trail;
    private readonly Queue<Point> points = new();
    private Point? lastPoint;
    public bool ShowTrail { get; set; } = true;
    public StickPlot()
    {
        var canvas = new Canvas { Width = 220, Height = 220 };
        var outer = new Ellipse { Width = 184, Height = 184, Stroke = new SolidColorBrush(Colors.LightGray), StrokeThickness = 1, Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 250, 250, 250)) };
        Canvas.SetLeft(outer, 18); Canvas.SetTop(outer, 18); canvas.Children.Add(outer);
        foreach (var line in new[] {
            new Line { X1=18, X2=202, Y1=110, Y2=110 }, new Line { X1=110, X2=110, Y1=18, Y2=202 } }) {
            line.Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 229, 229, 229)); canvas.Children.Add(line);
        }
        deadzoneCircle = new Ellipse { Fill = AppPalette.AccentBrush(30) };
        canvas.Children.Add(deadzoneCircle);
        trail = new Polyline { Stroke = AppPalette.AccentBrush(105), StrokeThickness = 2 };
        canvas.Children.Add(trail);
        rawDot = new Ellipse { Width = 11, Height = 11, Fill = AppPalette.AccentBrush() };
        outputDot = new Ellipse { Width = 16, Height = 16, Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 41, 41, 41)), StrokeThickness = 2 };
        canvas.Children.Add(outputDot); canvas.Children.Add(rawDot);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 220 };
        Update(new(0, 0), new(0, 0), .06);
    }
    public void Clear() { points.Clear(); trail.Points.Clear(); lastPoint = null; }
    public void Update(Stick raw, Stick output, double deadzone)
    {
        double radius = 92;
        Canvas.SetLeft(rawDot, 110 + raw.X * radius - 5.5); Canvas.SetTop(rawDot, 110 - raw.Y * radius - 5.5);
        Canvas.SetLeft(outputDot, 110 + output.X * radius - 8); Canvas.SetTop(outputDot, 110 - output.Y * radius - 8);
        deadzoneCircle.Width = deadzoneCircle.Height = radius * deadzone * 2;
        Canvas.SetLeft(deadzoneCircle, 110 - radius * deadzone); Canvas.SetTop(deadzoneCircle, 110 - radius * deadzone);
        var point = new Point(110 + raw.X * radius, 110 - raw.Y * radius);
        if (ShowTrail && point != lastPoint) {
            points.Enqueue(point); lastPoint = point;
            while (points.Count > 300) points.Dequeue();
            trail.Points = new PointCollection();
            foreach (var item in points) trail.Points.Add(item);
        }
        trail.Visibility = ShowTrail ? Visibility.Visible : Visibility.Collapsed;
    }
}
