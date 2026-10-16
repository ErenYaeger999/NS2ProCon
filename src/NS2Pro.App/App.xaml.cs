using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace NS2Pro.App;

public partial class App : Application
{
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
        instanceMutex = new Mutex(true, "Local\\NS2Pro.NativeApp", out bool first);
        if (!first) {
            var handle = NativeMethods.FindWindow(null, "NS2 Pro");
            if (handle != IntPtr.Zero) NativeMethods.ShowExisting(handle);
            Exit();
            return;
        }
        window = new MainWindow();
        window.Activate();
    }
}
