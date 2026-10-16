using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NS2Pro.App.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private const string ContactEmail = "945558278@qq.com";

    private void ConfigureSidebarFooter()
    {
        // Footer actions use the same native rows as the page navigation, including focus and hover.
        // SelectsOnInvoked=false keeps the current page and its live controls mounted.
        var contact = SidebarAction("联系与反馈", "\uE715");
        var quit = SidebarAction("退出应用", "\uE8AC");
        ToolTipService.SetToolTip(contact, "反馈问题、提出建议，或联系作者");
        ToolTipService.SetToolTip(quit, "断开 PC 连接并退出应用");
        AutomationProperties.SetHelpText(quit, "结束手柄的 PC 连接并完全退出；关闭窗口可继续留在托盘。");
        FlyoutBase.SetAttachedFlyout(contact, CreateContactFlyout());

        // WinUI places PaneFooter before FooterMenuItems; metadata must be the last footer item.
        navigation.PaneFooter = new Border { Height = 1, Margin = new(20, 12, 20, 8), Background = Brush(AppPalette.Border) };
        navigation.FooterMenuItems.Add(contact);
        navigation.FooterMenuItems.Add(quit);
        var version = Text($"v{AppVersion}", 11, false, AppPalette.Muted);
        version.HorizontalAlignment = HorizontalAlignment.Right;
        var metadata = new Grid { Margin = new(20, 12, 20, 18) };
        metadata.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        metadata.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        metadata.Children.Add(Text("NS2 Pro", 11, false, AppPalette.Muted));
        Grid.SetColumn(version, 1); metadata.Children.Add(version);
        var metadataItem = new NavigationViewItemHeader { Content = metadata, Style = (Style)Application.Current.Resources["SidebarMetadataStyle"] };
        AutomationProperties.SetName(metadataItem, $"NS2 Pro，版本 {AppVersion}");
        navigation.FooterMenuItems.Add(metadataItem);

        navigation.ItemInvoked += async (_, args) => {
            if (ReferenceEquals(args.InvokedItemContainer, contact)) FlyoutBase.ShowAttachedFlyout(contact);
            else if (ReferenceEquals(args.InvokedItemContainer, quit)) {
                quit.IsEnabled = false;
                try { await ExitAsync(); }
                catch (Exception error) {
                    exiting = false; quit.IsEnabled = true;
                    AppLog.Write("error", "退出未完成：" + error);
                    await ShowInfoAsync("退出未完成", error.Message);
                }
            }
        };
    }

    private static NavigationViewItem SidebarAction(string label, string glyph) => new() {
        Content = label, SelectsOnInvoked = false, FontSize = 13, MinHeight = 40, Margin = new(8, 2, 8, 2),
        Icon = new FontIcon { Glyph = glyph, FontFamily = new("Segoe Fluent Icons"), FontSize = 16 }
    };

    private static Flyout CreateContactFlyout()
    {
        var status = Text("", 12, false, AppPalette.Muted); status.Visibility = Visibility.Collapsed;
        var copy = new Button { Content = "复制邮箱", Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        copy.Click += (_, _) => {
            try {
                var data = new DataPackage(); data.SetText(ContactEmail); Clipboard.SetContent(data);
                copy.Content = "已复制"; status.Visibility = Visibility.Collapsed;
            } catch (Exception) { status.Text = "暂时无法复制，可选中上方邮箱手动复制。"; status.Visibility = Visibility.Visible; }
        };
        var mail = new Button { Content = "写邮件", Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
        mail.Click += async (_, _) => {
            try {
                if (await Launcher.LaunchUriAsync(new Uri($"mailto:{ContactEmail}?subject=NS2%20Pro%20%E5%8F%8D%E9%A6%88"))) return;
            } catch (Exception) { }
            status.Text = "未能打开邮件应用，可复制邮箱后联系。"; status.Visibility = Visibility.Visible;
        };
        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        actions.Children.Add(copy); Grid.SetColumn(mail, 1); actions.Children.Add(mail);
        var email = Text(ContactEmail, 16, true); email.IsTextSelectionEnabled = true;
        var flyout = new Flyout { Placement = FlyoutPlacementMode.RightEdgeAlignedBottom, Content = new StackPanel { Width = 260, Spacing = 16, Children = {
            new StackPanel { Spacing = 5, Children = { Text("联系与反馈", 17, true), Text("遇到问题或有建议，欢迎联系。", 12, false, AppPalette.Muted) } },
            email, actions, status
        } } };
        flyout.Opening += (_, _) => { copy.Content = "复制邮箱"; status.Visibility = Visibility.Collapsed; };
        return flyout;
    }
}
