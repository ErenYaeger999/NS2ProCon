using System.Runtime.InteropServices;
using NS2Pro.App.Services;

namespace NS2Pro.App;

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBox(IntPtr window, string text, string caption, uint type);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
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
    private readonly Action show, exit, connect, disconnect;
    private readonly uint taskbarCreated;
    private ConnectionPhase phase = ConnectionPhase.Idle;
    private string tip = App.WindowTitle + " · 未连接 · 右键连接手柄";
    private bool disposed;
    public TrayIcon(IntPtr window, Action show, Action exit, Action connect, Action disconnect)
    {
        this.window = window; this.show = show; this.exit = exit; this.connect = connect; this.disconnect = disconnect;
        int pixels = Math.Max(16, NativeMethods.GetSystemMetricsForDpi(49, NativeMethods.GetDpiForWindow(window)));
        icon = NativeMethods.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"), 1, pixels, pixels, 0x10);
        if (icon == IntPtr.Zero) throw new InvalidOperationException("无法加载托盘图标");
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        procedure = HandleMessage;
        if (!SetWindowSubclass(window, procedure, 1, UIntPtr.Zero)) {
            _ = NativeMethods.DestroyIcon(icon); throw new InvalidOperationException("托盘窗口初始化失败");
        }
        if (!RegisterIcon()) {
            _ = RemoveWindowSubclass(window, procedure, 1); _ = NativeMethods.DestroyIcon(icon);
            throw new InvalidOperationException("无法创建托盘图标");
        }
        AppLog.Write("info", "系统托盘已就绪，可右键连接手柄");
    }
    private NotifyIconData Data() => new() {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, CallbackMessage = Callback, Icon = icon, Tip = tip,
        Info = "", InfoTitle = ""
    };
    private bool RegisterIcon()
    {
        var data = Data();
        if (!Shell_NotifyIcon(0, ref data)) return false;
        data.TimeoutOrVersion = 4;
        _ = Shell_NotifyIcon(4, ref data);
        return true;
    }
    public void Update(ConnectionPhase next)
    {
        if (disposed || phase == next) return;
        phase = next;
        string state = phase switch {
            ConnectionPhase.Connected => "已连接到 PC",
            ConnectionPhase.WaitingForInput => "蓝牙已连接 · 等待手柄输入",
            ConnectionPhase.Searching => "请长按手柄顶部充电口旁的小键",
            ConnectionPhase.Preparing or ConnectionPhase.Connecting => "正在连接手柄",
            ConnectionPhase.Stopping => "正在断开",
            ConnectionPhase.Error => "连接未完成 · 右键重试",
            _ => "未连接 · 右键连接手柄"
        };
        tip = App.WindowTitle + " · " + state;
        var data = Data(); data.Flags = 4 | 0x80;
        _ = Shell_NotifyIcon(1, ref data);
    }
    private IntPtr HandleMessage(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data)
    {
        if (disposed) return DefSubclassProc(hwnd, message, wparam, lparam);
        if (taskbarCreated != 0 && message == taskbarCreated) {
            if (!RegisterIcon()) AppLog.Write("warn", "Explorer 重启后暂时无法恢复托盘图标");
            return IntPtr.Zero;
        }
        if (message == Callback) {
            uint action = (uint)lparam.ToInt64() & 0xffff;
            if (action is 0x202 or 0x203 or 0x400 or 0x401) show();
            if (action is 0x205 or 0x7b) {
                var menu = CreatePopupMenu();
                if (menu == IntPtr.Zero) return IntPtr.Zero;
                uint command;
                try {
                    AppendMenu(menu, 0, 1, "打开 " + AppIdentity.DisplayName);
                    var state = new ConnectionInfo(phase, "");
                    bool active = state.CanStop;
                    string label = phase == ConnectionPhase.Idle ? "连接手柄（长按顶部充电口旁的小键）" : state.ActionText;
                    AppendMenu(menu, AppSettings.IsPreview || phase == ConnectionPhase.Stopping ? 1u : 0u, active ? 4u : 3u, label);
                    AppendMenu(menu, 0x800, 0, "");
                    AppendMenu(menu, 0, 2, "断开并退出");
                    GetCursorPos(out var point);
                    NativeMethods.SetForegroundWindow(window);
                    command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero);
                } finally { _ = DestroyMenu(menu); }
                var notification = Data(); _ = Shell_NotifyIcon(3, ref notification);
                if (command == 1) show();
                else if (command == 2) exit();
                else if (command == 3) connect();
                else if (command == 4) disconnect();
            }
            return IntPtr.Zero;
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
