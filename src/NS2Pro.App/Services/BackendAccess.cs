using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using NS2Pro.Core;

namespace NS2Pro.App.Services;

internal static class BackendAccess
{
    private static readonly object Gate = new();
    private static (int ProcessId, byte[] Key)? owner;
    [SupportedOSPlatform("windows")]
    public static string CreatePrivateKey(string directory)
    {
        var folder = Directory.CreateDirectory(directory);
        var access = new DirectorySecurity(); access.SetAccessRuleProtection(true, false);
        using var identityContext = WindowsIdentity.GetCurrent();
        var user = identityContext.User ?? throw new IOException("无法读取当前用户身份。");
        access.SetOwner(user);
        foreach (var identity in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            access.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        folder.SetAccessControl(access);
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(Path.Combine(directory, "backend.key"), password);
        return password;
    }
    public static void Register(int processId, string password)
    {
        lock (Gate) { if (owner is not null) CryptographicOperations.ZeroMemory(owner.Value.Key); owner = (processId, ViiperSecureStream.DeriveKey(password)); }
    }
    public static void Clear()
    {
        lock (Gate) { if (owner is not null) CryptographicOperations.ZeroMemory(owner.Value.Key); owner = null; }
    }
    public static async Task<Stream> OpenAsync(TcpClient client, int port, CancellationToken token)
    {
        (int ProcessId, byte[] Key)? access;
        lock (Gate) access = port == EnhancementComponents.ApiPort && owner is { } item ? (item.ProcessId, item.Key.ToArray()) : null;
        if (access is null) return client.GetStream(); // explicitly configured external backend
        try {
            RequireListener(port, access.Value.ProcessId);
            return await ViiperSecureStream.ConnectAsync(client.GetStream(), access.Value.Key, token).ConfigureAwait(false);
        } finally { CryptographicOperations.ZeroMemory(access.Value.Key); }
    }
    public static void RequireListener(int port, int processId)
    {
        int size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        if (size <= 0 || size > 16_000_000) throw new IOException("无法确认增强后端端口归属。");
        var memory = Marshal.AllocHGlobal(size);
        try {
            if (GetExtendedTcpTable(memory, ref size, false, 2, 3, 0) != 0) throw new IOException("无法确认增强后端端口归属。");
            int count = Marshal.ReadInt32(memory);
            if (count < 0 || count > (size - 4) / 24) throw new IOException("端口表无效。");
            for (int i = 0; i < count; i++) {
                var row = memory + 4 + i * 24;
                int raw = Marshal.ReadInt32(row, 8), actual = ((raw & 255) << 8) | ((raw >> 8) & 255);
                if (actual == port && Marshal.ReadInt32(row, 4) == BitConverter.ToInt32(IPAddress.Loopback.GetAddressBytes()) && Marshal.ReadInt32(row, 20) == processId) return;
            }
            throw new IOException("增强端口不属于本应用启动的后端，已拒绝连接。");
        } finally { Marshal.FreeHGlobal(memory); }
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
}
