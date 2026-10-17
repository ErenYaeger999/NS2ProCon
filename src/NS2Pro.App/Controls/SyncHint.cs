using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;

namespace NS2Pro.App.Controls;

/// <summary>One consistent location hint for the controller's unlabelled pairing button.</summary>
public static class SyncHint
{
    public const string Location = "手柄顶部充电口旁边的按键";

    public static Button CreateIcon()
    {
        var icon = new Button {
            Width = 14, Height = 14, MinWidth = 0, MinHeight = 0, Padding = new(1),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new(0),
            VerticalAlignment = VerticalAlignment.Top,
            Content = new FontIcon { Glyph = "\uE946", FontFamily = new("Segoe MDL2 Assets"), FontSize = 10,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 144, 144, 144)) },
            Flyout = new Flyout { Content = new TextBlock { Text = Location, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 } }
        };
        ToolTipService.SetToolTip(icon, Location);
        AutomationProperties.SetName(icon, "SYNC 按键位置");
        AutomationProperties.SetHelpText(icon, Location);
        return icon;
    }

    public static Grid Wrap(FrameworkElement content)
    {
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(content);
        var icon = CreateIcon(); Grid.SetColumn(icon, 1); row.Children.Add(icon);
        return row;
    }

    // Dynamic connection messages lose the hint when they no longer mention SYNC.
    public static RichTextBlock ForText(TextBlock text)
    {
        var result = new RichTextBlock { IsTextSelectionEnabled = false, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = text.HorizontalAlignment, VerticalAlignment = text.VerticalAlignment, Margin = text.Margin };
        var paragraph = new Paragraph(); result.Blocks.Add(paragraph);
        void Style() {
            result.FontSize = text.FontSize; result.FontFamily = text.FontFamily;
            result.FontWeight = text.FontWeight; result.Foreground = text.Foreground;
            result.TextAlignment = text.TextAlignment; result.Visibility = text.Visibility;
        }
        void Refresh() {
            paragraph.Inlines.Clear();
            string value = text.Text; int cursor = 0, index;
            while ((index = value.IndexOf("SYNC", cursor, StringComparison.OrdinalIgnoreCase)) >= 0) {
                // Keep SYNC in the same text flow as Chinese, so DirectWrite shares its baseline.
                // A word joiner keeps the following hint attached when the paragraph wraps.
                paragraph.Inlines.Add(new Run { Text = value[cursor..(index + 4)] + "\u2060" });
                var icon = CreateIcon(); icon.Margin = new(1, 0, 2, 0);
                paragraph.Inlines.Add(new InlineUIContainer { Child = icon });
                cursor = index + 4;
            }
            if (cursor < value.Length) paragraph.Inlines.Add(new Run { Text = value[cursor..] });
            Style();
        }
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Refresh());
        text.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Style());
        foreach (var property in new[] { TextBlock.ForegroundProperty, TextBlock.FontSizeProperty, TextBlock.FontWeightProperty, TextBlock.FontFamilyProperty })
            text.RegisterPropertyChangedCallback(property, (_, _) => Refresh());
        Refresh(); return result;
    }
}
