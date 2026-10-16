using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace NS2Pro.App.Controls;

/// <summary>Native vector reconstruction of the Switch 2 mark printed on the controller.</summary>
public sealed class Switch2Mark : UserControl
{
    // Reference: Nintendo's section-hero-logo.svg and the official Pro Controller front photograph.
    public Switch2Mark()
    {
        IsHitTestVisible = false;
        var ink = new SolidColorBrush(ColorHelper.FromArgb(255, 25, 34, 39));
        var canvas = new Canvas { Width = 391, Height = 330 };
        Add(canvas, "F0 M65 0 H100 V200 H65 C29 200 0 171 0 135 V65 C0 29 29 0 65 0 Z M65 18 C39 18 18 39 18 65 V135 C18 161 39 182 65 182 H82 V18 Z M68 65 A19 19 0 1 1 30 65 A19 19 0 1 1 68 65 Z", ink);
        Add(canvas, "F0 M117 0 H150 C186 0 215 29 215 65 V135 C215 171 186 200 150 200 H117 Z M188 132 A20 20 0 1 1 148 132 A20 20 0 1 1 188 132 Z", ink);
        Add(canvas, "M235 45 C252 14 278 0 314 0 C361 0 389 23 389 62 C389 90 372 110 344 133 L303 167 H391 V200 H236 V173 L311 110 C338 87 351 75 351 62 C351 44 337 35 317 35 C298 35 282 46 270 63 Z", ink);
        Word(canvas, "NINTENDO", 215, 34, 270, 320, ink);
        Word(canvas, "SWITCH", 258, 59, 20, 391, ink);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(this, "Nintendo Switch 2 标志");
    }

    private static void Add(Canvas canvas, string data, Brush fill)
    {
        var path = (ShapePath)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}'/>");
        path.Fill = fill; canvas.Children.Add(path);
    }

    private static void Word(Canvas canvas, string word, double y, double size, int spacing, double width, Brush ink)
    {
        var text = new TextBlock { Text = word, FontSize = size, FontFamily = new("Arial"), FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = spacing, Foreground = ink, TextAlignment = TextAlignment.Center };
        var wordmark = new Viewbox { Width = width, Height = size * 1.1, Stretch = Stretch.Fill, Child = text };
        Canvas.SetLeft(wordmark, (391 - width) / 2); Canvas.SetTop(wordmark, y); canvas.Children.Add(wordmark);
    }
}
