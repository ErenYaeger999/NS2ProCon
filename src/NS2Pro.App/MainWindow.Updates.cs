using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NS2Pro.App.Services;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private InfoBar? updateNotice;
    private Button? updateButton;
    private TextBlock? updateResult;
    private Task<UpdateCheckResult>? updateCheck;
    private readonly UpdateService updateService = new();
    private readonly CancellationTokenSource updateCancellation = new();
    private string updateMessage = "启动后自动检查正式版更新。";

    private InfoBar CreateUpdateNotice()
    {
        updateNotice = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Informational,
            Visibility = Visibility.Collapsed, Margin = new(0, 12, 24, 0),
            ActionButton = ReleaseLink() };
        updateNotice.Closed += (_, _) => updateNotice.Visibility = Visibility.Collapsed;
        return updateNotice;
    }

    private static HyperlinkButton ReleaseLink() => new() {
        Content = "前往 Release 下载", NavigateUri = new Uri(UpdateService.ReleasesUrl), Padding = new(0, 6, 0, 6)
    };

    private UIElement UpdateSettingsCard()
    {
        updateButton = new Button { Content = "检查更新" };
        updateButton.Click += async (_, _) => await CheckUpdatesAsync();
        updateButton.IsEnabled = updateCheck is not { IsCompleted: false };
        updateResult = Text(updateMessage, 12, false, AppPalette.Muted);
        return Card(new StackPanel { Spacing = 12, Children = {
            Text("版本更新", 17, true), Text($"当前版本 v{AppVersion}", 13),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { updateButton, ReleaseLink() } },
            updateResult
        } }, 24);
    }

    private async Task CheckUpdatesAsync()
    {
        if (exiting || AppSettings.IsPreview) return;
        if (updateButton is not null) updateButton.IsEnabled = false;
        updateMessage = "正在检查更新…";
        if (updateResult is not null) updateResult.Text = updateMessage;
        try {
            if (updateCheck is null || updateCheck.IsCompleted)
                updateCheck = Task.Run(() => updateService.CheckAsync(typeof(MainWindow).Assembly.GetName().Version!, updateCancellation.Token));
            var result = await updateCheck;
            if (exiting) return;
            if (result.Detail is { } detail) AppLog.Write(result.State == UpdateCheckState.Unavailable ? "warn" : "info", "检查更新：" + detail);
            ShowUpdateResult(result);
        } finally { if (updateButton is not null) updateButton.IsEnabled = true; }
    }

    private void ShowUpdateResult(UpdateCheckResult result)
    {
        updateMessage = result.State switch {
            UpdateCheckState.Available => $"发现新版本 v{result.LatestVersion}，可前往 Release 下载。安装包会沿用原安装位置。",
            UpdateCheckState.Current => $"当前版本无需更新 · 最新正式版 v{result.LatestVersion}",
            _ => "暂时无法检查更新，可稍后重试或前往 Release 查看。"
        };
        if (updateResult is not null) updateResult.Text = updateMessage;
        if (result.State == UpdateCheckState.Current && updateNotice is not null) {
            updateNotice.IsOpen = false; updateNotice.Visibility = Visibility.Collapsed;
        }
        if (result.State != UpdateCheckState.Available || updateNotice is null) return;
        updateNotice.Title = $"发现新版本 v{result.LatestVersion}";
        updateNotice.Message = "前往 Release 查看说明并下载。安装前请退出应用，安装包会沿用原位置更新。";
        updateNotice.Visibility = Visibility.Visible;
        updateNotice.IsOpen = true;
    }
}
