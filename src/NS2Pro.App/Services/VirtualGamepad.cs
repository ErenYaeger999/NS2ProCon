using NS2Pro.Core;
using System.Runtime.InteropServices;

namespace NS2Pro.App.Services;

public sealed class VirtualGamepad : IGameOutput
{
    private IntPtr bus, target;
    private readonly object gate = new();
    private const uint Success = 0x20000000;
    private readonly Notification notification;
    private volatile bool acceptingFeedback;
    private bool registered;
    private int disposeState;
    public bool FeedbackAvailable => registered;
    public event Action<byte, byte>? RumbleChanged;
    public event Action<long>? InputForwarded;
    public VirtualGamepad()
    {
        notification = OnFeedback;
        try {
            bus = vigem_alloc();
            if (bus == IntPtr.Zero) throw new InvalidOperationException("无法初始化虚拟手柄客户端");
            Check(vigem_connect(bus));
            target = vigem_target_x360_alloc();
            if (target == IntPtr.Zero) throw new InvalidOperationException("无法创建 Xbox 360 手柄");
            Check(vigem_target_add(bus, target));
            Update(default);
            acceptingFeedback = true;
            try { registered = vigem_target_x360_register_notification(bus, target, notification, IntPtr.Zero) == Success; }
            catch (EntryPointNotFoundException) { registered = false; }
        } catch { Dispose(); throw; }
    }
    private void OnFeedback(IntPtr client, IntPtr device, byte large, byte small, byte led, IntPtr userData)
    {
        if (!acceptingFeedback) return;
        try { RumbleChanged?.Invoke(large, small); }
        catch (Exception) { /* Never unwind managed exceptions through a native driver callback. */ }
    }
    public void Update(XboxReport report, long receivedAt = 0)
    {
        lock (gate) { if (target != IntPtr.Zero) Check(vigem_target_x360_update(bus, target, report)); }
        if (receivedAt > 0) InputForwarded?.Invoke(receivedAt);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0) return;
        // unregister waits for the native callback thread: never hold the update lock here.
        acceptingFeedback = false;
        if (registered) { vigem_target_x360_unregister_notification(target); registered = false; }
        lock (gate) {
            if (target != IntPtr.Zero) {
                _ = vigem_target_x360_update(bus, target, default);
                _ = vigem_target_remove(bus, target);
                vigem_target_free(target);
                target = IntPtr.Zero;
            }
            if (bus != IntPtr.Zero) { vigem_disconnect(bus); vigem_free(bus); bus = IntPtr.Zero; }
        }
        GC.KeepAlive(notification);
    }
    public static string CheckDriver()
    {
        try { EnsureDriverAvailable(); return "ViGEmBus 已就绪"; }
        catch (Exception error) { return error.Message; }
    }
    public static void EnsureDriverAvailable()
    {
        // Probe the bus without adding an Xbox device to a running game.
        var client = vigem_alloc();
        if (client == IntPtr.Zero) throw new InvalidOperationException("无法初始化虚拟手柄客户端");
        try { Check(vigem_connect(client)); }
        finally { vigem_disconnect(client); vigem_free(client); }
    }
    private static void Check(uint code)
    {
        if (code == Success) return;
        throw new InvalidOperationException(code == 0xe0000001 ?
            "未找到 ViGEmBus 驱动，请先安装官方驱动" : $"虚拟手柄错误 0x{code:X8}");
    }
    private const string Library = "ViGEmClient.dll";
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void Notification(IntPtr client, IntPtr device, byte large, byte small, byte led, IntPtr userData);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint vigem_target_x360_register_notification(IntPtr client, IntPtr device, Notification callback, IntPtr userData);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void vigem_target_x360_unregister_notification(IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr vigem_alloc();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void vigem_free(IntPtr client);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint vigem_connect(IntPtr client);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void vigem_disconnect(IntPtr client);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr vigem_target_x360_alloc();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void vigem_target_free(IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint vigem_target_add(IntPtr client, IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint vigem_target_remove(IntPtr client, IntPtr device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint vigem_target_x360_update(IntPtr client, IntPtr device, XboxReport report);
}
