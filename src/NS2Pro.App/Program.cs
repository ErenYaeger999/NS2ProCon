using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NS2Pro.App.Services;

namespace NS2Pro.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new SingleInstance();
        if (!instance.IsPrimary) {
            // Legacy versions do not listen for activation; restore their window directly too.
            IntPtr handle = IntPtr.Zero;
            foreach (var title in new[] { AppIdentity.DisplayName, AppIdentity.PreviewTitle, "NS2 Pro", "NS2 Pro · 引导预览" }) {
                handle = NativeMethods.FindWindow(null, title);
                if (handle != IntPtr.Zero) break;
            }
            if (handle != IntPtr.Zero) {
                _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
                _ = NativeMethods.AllowSetForegroundWindow(processId);
                NativeMethods.ShowExisting(handle);
            }
            instance.RequestActivation();
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App(instance);
        });
    }
}
