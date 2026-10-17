using System.Runtime.InteropServices;

namespace NS2Pro.App;

internal sealed class PowerNotifications : IDisposable
{
    private readonly IntPtr window;
    private readonly SubclassProc procedure;
    private readonly Action<bool> changed;
    public PowerNotifications(IntPtr window, Action<bool> changed)
    {
        this.window = window; this.changed = changed; procedure = Receive;
        if (!SetWindowSubclass(window, procedure, 2, UIntPtr.Zero)) throw new InvalidOperationException("无法订阅休眠通知");
    }
    private IntPtr Receive(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data)
    {
        if (message == 0x218) {
            try {
                if (wparam.ToUInt64() == 4) changed(true);
                else if (wparam.ToUInt64() is 7 or 18) changed(false);
            } catch (Exception) { }
        }
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    public void Dispose() { _ = RemoveWindowSubclass(window, procedure, 2); GC.KeepAlive(procedure); }
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
}
