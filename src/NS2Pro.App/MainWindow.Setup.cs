using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NS2Pro.App.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private ConnectionGuideView? connectionGuide;
    private Button ContactEntry()
    {
        var copy = new Button { Content = "复制邮箱", FontSize = 12 };
        copy.Click += (_, _) => {
            var data = new DataPackage(); data.SetText("945558278@qq.com"); Clipboard.SetContent(data);
            copy.Content = "已复制";
        };
        var mail = new HyperlinkButton { Content = "发送邮件", NavigateUri = new Uri("mailto:945558278@qq.com?subject=NS2%20Pro%20%E5%8F%8D%E9%A6%88"), FontSize = 12 };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 12, Children = {
            Text("联系作者", 15, true), Text("945558278@qq.com", 13),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copy, mail } }
        } } };
        flyout.Opening += (_, _) => copy.Content = "复制邮箱";
        var entry = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = {
            new FontIcon { Glyph = "\uE715", FontSize = 13 }, Text("联系作者", 12, false, "666666")
        } }, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Padding = new(8, 6, 8, 6), Flyout = flyout };
        AutomationProperties.SetName(entry, "联系作者，945558278@qq.com");
        return entry;
    }

    private void BuildSetupPage()
    {
        title.Text = "连接指南"; subtitle.Text = "第一次连接，跟着这三步来。";
        connectionGuide = new ConnectionGuideView(settings, controller, () => SelectPage("test"), () => SelectPage("overview"));
        connectionGuide.InitialCheckCompleted += () => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
            if (selectedPage != "setup") return;
            if (navigation.SelectedItem is NavigationViewItem item) item.Focus(FocusState.Programmatic);
            pageScroll.ChangeView(null, 0, null, true);
        });
        page.Children.Add(connectionGuide);
    }
}
