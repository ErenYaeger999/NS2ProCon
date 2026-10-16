using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NS2Pro.Core;
using Windows.UI;

namespace NS2Pro.App.Controls;

public sealed class ControllerView : UserControl
{
    private readonly Dictionary<string, Border> buttons = new();
    private readonly Ellipse leftThumb, rightThumb;
    private readonly TranslateTransform leftMovement = new(), rightMovement = new();
    private static SolidColorBrush Brush(string hex) => new(ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
    public ControllerView()
    {
        IsHitTestVisible = false;
        var canvas = new Canvas { Width = 560, Height = 330 };
        var body = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("""
          <Path xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           Data="M 144,62 C 116,60 102,80 89,116 L 51,254 C 40,298 67,315 96,293 L 164,241 C 192,232 368,232 396,241 L 464,293 C 493,315 520,298 509,254 L 471,116 C 458,80 444,60 416,62 L 144,62 Z"
           Fill="#EEF1F1" Stroke="#D8DEDF" StrokeThickness="2"/>
          """);
        canvas.Children.Add(body);
        AddButton(canvas, "ZL", 117, 34, 82, 25, 12);
        AddButton(canvas, "ZR", 361, 34, 82, 25, 12);
        AddButton(canvas, "L", 114, 65, 90, 25, 12);
        AddButton(canvas, "R", 358, 65, 90, 25, 12);
        AddCircle(canvas, 120, 100, 91, "D2DADB");
        leftThumb = AddCircle(canvas, 132, 112, 67, "35454B");
        leftThumb.RenderTransform = leftMovement;
        AddCircle(canvas, 320, 169, 91, "D2DADB");
        rightThumb = AddCircle(canvas, 332, 181, 67, "35454B");
        rightThumb.RenderTransform = rightMovement;
        AddButton(canvas, "X", 402, 98, 35, 35, 18);
        AddButton(canvas, "Y", 366, 134, 35, 35, 18);
        AddButton(canvas, "A", 438, 134, 35, 35, 18);
        AddButton(canvas, "B", 402, 170, 35, 35, 18);
        AddButton(canvas, "↑", 206, 175, 29, 29, 6);
        AddButton(canvas, "←", 176, 204, 29, 29, 6);
        AddButton(canvas, "→", 236, 204, 29, 29, 6);
        AddButton(canvas, "↓", 206, 233, 29, 29, 6);
        AddButton(canvas, "−", 235, 111, 27, 24, 6);
        AddButton(canvas, "+", 297, 111, 27, 24, 6);
        AddButton(canvas, "⌂", 296, 151, 27, 27, 14);
        AddButton(canvas, "□", 249, 199, 24, 24, 7);
        AddButton(canvas, "C", 274, 159, 19, 19, 10);
        AddButton(canvas, "GL", 143, 273, 36, 20, 8);
        AddButton(canvas, "GR", 381, 273, 36, 20, 8);
        var label = new TextBlock { Text = "NS2 PRO", FontSize = 9, CharacterSpacing = 220, Foreground = Brush("879497") };
        Canvas.SetLeft(label, 250); Canvas.SetTop(label, 82); canvas.Children.Add(label);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 320 };
        AutomationProperties.SetName(this, "Switch 2 Pro 手柄实时按键预览");
    }
    private static Ellipse AddCircle(Canvas canvas, double x, double y, double size, string fill)
    {
        var circle = new Ellipse { Width = size, Height = size, Fill = Brush(fill) };
        Canvas.SetLeft(circle, x); Canvas.SetTop(circle, y); canvas.Children.Add(circle); return circle;
    }
    private void AddButton(Canvas canvas, string label, double x, double y, double w, double h, double radius)
    {
        var button = new Border { Width = w, Height = h, CornerRadius = new(radius), Background = Brush("DCE2E3"),
            Child = new TextBlock { Text = label, FontSize = label.Length > 1 ? 10 : 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush("46585F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        Canvas.SetLeft(button, x); Canvas.SetTop(button, y); canvas.Children.Add(button); buttons[label] = button;
    }
    public void Update(ControllerInput? state)
    {
        var right = state?.RightButtons ?? 0;
        var left = state?.LeftButtons ?? 0;
        var extra = state?.ExtraButtons ?? 0;
        var flags = new Dictionary<string, bool> {
            ["B"] = (right & 1) != 0, ["A"] = (right & 2) != 0, ["Y"] = (right & 4) != 0, ["X"] = (right & 8) != 0,
            ["R"] = (right & 16) != 0, ["ZR"] = (right & 32) != 0, ["+"] = (right & 64) != 0,
            ["↓"] = (left & 1) != 0, ["→"] = (left & 2) != 0, ["←"] = (left & 4) != 0, ["↑"] = (left & 8) != 0,
            ["L"] = (left & 16) != 0, ["ZL"] = (left & 32) != 0, ["−"] = (left & 64) != 0,
            ["⌂"] = (extra & 1) != 0, ["□"] = (extra & 2) != 0, ["GR"] = (extra & 4) != 0, ["GL"] = (extra & 8) != 0, ["C"] = (extra & 16) != 0
        };
        foreach (var (label, button) in buttons) {
            var pressed = flags.GetValueOrDefault(label);
            button.Background = Brush(pressed ? "187B78" : "DCE2E3");
            ((TextBlock)button.Child).Foreground = Brush(pressed ? "FFFFFF" : "46585F");
        }
        leftThumb.Fill = Brush((left & 128) != 0 ? "187B78" : "35454B");
        rightThumb.Fill = Brush((right & 128) != 0 ? "187B78" : "35454B");
        leftMovement.X = (state?.UnfilteredLeft.X ?? 0) * 10; leftMovement.Y = -(state?.UnfilteredLeft.Y ?? 0) * 10;
        rightMovement.X = (state?.UnfilteredRight.X ?? 0) * 10; rightMovement.Y = -(state?.UnfilteredRight.Y ?? 0) * 10;
    }
}
