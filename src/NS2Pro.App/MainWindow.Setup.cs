using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NS2Pro.App.Controls;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private ConnectionGuideView? connectionGuide;
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
