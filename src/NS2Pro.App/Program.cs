using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NS2Pro.App.Services;
using System.Diagnostics;

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
            bool otherCopy = false;
            if (handle != IntPtr.Zero) {
                _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
                _ = NativeMethods.AllowSetForegroundWindow(processId);
                NativeMethods.ShowExisting(handle);
                try {
                    using var running = Process.GetProcessById((int)processId);
                    var runningPath = running.MainModule?.FileName;
                    otherCopy = runningPath is not null && !string.Equals(Path.GetFullPath(runningPath), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
                    if (otherCopy) AppLog.Write("info", $"另一位置的应用仍在运行：{runningPath}；本次打开：{Environment.ProcessPath}");
                } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) {
                    AppLog.Write("warn", "无法确认运行副本位置：" + error.Message);
                }
            }
            instance.RequestActivation();
            if (otherCopy) NativeMethods.MessageBox(handle,
                "另一位置的应用仍在运行，本次已返回原窗口。\n\n如果要使用刚下载或更新的版本，请先退出当前应用，再重新打开新版本。关闭到后台时，请从托盘菜单选择「断开并退出」。\n\n已安装的增强组件会保留。",
                AppIdentity.DisplayName, 0x40);
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App(instance);
        });
    }
}
