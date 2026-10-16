using System.Runtime.InteropServices;

namespace NS2Pro.App;

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    internal static void ShowExisting(IntPtr hwnd) { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
}

/// <summary>Small Win32 notification icon; the interface itself remains WinUI 3.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint Callback = 0x8000 + 71;
    private readonly IntPtr window, icon;
    private readonly SubclassProc procedure;
    private readonly Action show, exit;
    private bool disposed;
    public TrayIcon(IntPtr window, Action show, Action exit)
    {
        this.window = window; this.show = show; this.exit = exit;
        icon = NativeMethods.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"), 1, 0, 0, 0x10 | 0x40);
        procedure = HandleMessage;
        if (!SetWindowSubclass(window, procedure, 1, UIntPtr.Zero)) throw new InvalidOperationException("托盘窗口初始化失败");
        var data = Data();
        if (!Shell_NotifyIcon(0, ref data)) throw new InvalidOperationException("无法创建托盘图标");
    }
    private NotifyIconData Data() => new() {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
        Flags = 1 | 2 | 4, CallbackMessage = Callback, Icon = icon, Tip = "NS2 Pro · 手柄无线连接",
        Info = "", InfoTitle = ""
    };
    private IntPtr HandleMessage(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data)
    {
        if (message == Callback) {
            uint action = (uint)lparam.ToInt64() & 0xffff;
            if (action is 0x202 or 0x203) show();
            if (action == 0x205) {
                var menu = CreatePopupMenu();
                AppendMenu(menu, 0, 1, "打开 NS2 Pro");
                AppendMenu(menu, 0x800, 0, "");
                AppendMenu(menu, 0, 2, "断开并退出");
                GetCursorPos(out var point);
                NativeMethods.SetForegroundWindow(window);
                uint command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero);
                DestroyMenu(menu);
                if (command == 1) show();
                else if (command == 2) exit();
            }
        }
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var data = Data();
        _ = Shell_NotifyIcon(2, ref data);
        _ = RemoveWindowSubclass(window, procedure, 1);
        if (icon != IntPtr.Zero) _ = NativeMethods.DestroyIcon(icon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData {
        public uint Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, UIntPtr wparam, IntPtr lparam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
}
