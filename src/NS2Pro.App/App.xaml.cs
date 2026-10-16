using Microsoft.UI.Xaml;
using NS2Pro.App.Services;

namespace NS2Pro.App;

public partial class App : Application
{
    internal static string WindowTitle => Services.AppSettings.IsPreview ? "NS2 Pro · 引导预览" : "NS2 Pro";
    private MainWindow? window;
    private readonly SingleInstance instance;
    internal App(SingleInstance instance)
    {
        this.instance = instance;
        InitializeComponent();
        UnhandledException += (_, args) => {
            Services.AppLog.Write("error", args.Exception.ToString());
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        var dispatcher = window.DispatcherQueue;
        instance.Listen(() => dispatcher.TryEnqueue(() => {
            NativeMethods.ShowExisting(WinRT.Interop.WindowNative.GetWindowHandle(window));
            window.Activate();
        }));
        window.Activate();
    }
}
