using NS2Pro.Core;
using System.Runtime.InteropServices;

namespace NS2Pro.App.Services;

public sealed class VirtualGamepad : IDisposable
{
    private IntPtr bus, target;
    private readonly object gate = new();
    private const uint Success = 0x20000000;
    public VirtualGamepad()
    {
        try {
            bus = vigem_alloc();
            if (bus == IntPtr.Zero) throw new InvalidOperationException("无法初始化虚拟手柄客户端");
            Check(vigem_connect(bus));
            target = vigem_target_x360_alloc();
            if (target == IntPtr.Zero) throw new InvalidOperationException("无法创建 Xbox 360 手柄");
            Check(vigem_target_add(bus, target));
            Update(default);
        } catch { Dispose(); throw; }
    }
    public void Update(XboxReport report)
    {
        lock (gate) { if (target != IntPtr.Zero) Check(vigem_target_x360_update(bus, target, report)); }
    }
    public void Dispose()
    {
        lock (gate) {
            if (target != IntPtr.Zero) {
                _ = vigem_target_x360_update(bus, target, default);
                _ = vigem_target_remove(bus, target);
                vigem_target_free(target);
                target = IntPtr.Zero;
            }
            if (bus != IntPtr.Zero) { vigem_disconnect(bus); vigem_free(bus); bus = IntPtr.Zero; }
        }
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
