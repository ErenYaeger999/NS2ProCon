using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace NS2Pro.App;

internal static class AppPalette
{
    // Reference: https://www.nintendo.com/jp/common/v3/css/shared.css
    public const string Accent = "E60012";
    public const string AccentText = "C60010";
    public const string Soft = "FFF1F2";
    public const string SoftStrong = "FCE4E6";
    public const string Page = "F7F7F7";
    public const string Border = "E5E5E5";
    public const string Text = "292929";
    public const string Muted = "707070";
    public static SolidColorBrush AccentBrush(byte alpha = 255) => new(ColorHelper.FromArgb(alpha, 230, 0, 18));
}
