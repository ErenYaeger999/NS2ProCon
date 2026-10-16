using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace NS2Pro.App;

public partial class App : Application
{
    internal static string WindowTitle => Services.AppSettings.IsPreview ? "NS2 Pro · 引导预览" : "NS2 Pro";
    private MainWindow? window;
    private Mutex? instanceMutex;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => {
            Services.AppLog.Write("error", args.Exception.ToString());
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        instanceMutex = new Mutex(true, "Local\\NS2Pro.NativeApp" + (Services.AppSettings.IsPreview ? ".Preview" : ""), out bool first);
        if (!first) {
            var handle = NativeMethods.FindWindow(null, WindowTitle);
            if (handle != IntPtr.Zero) NativeMethods.ShowExisting(handle);
            Exit();
            return;
        }
        window = new MainWindow();
        window.Activate();
    }
}
