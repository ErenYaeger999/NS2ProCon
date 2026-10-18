using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NS2Pro.Core;

namespace NS2Pro.App.Services;

/// <summary>Core Audio endpoint IDs and per-role defaults. Never changes gain, mute or device state.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAudioDefaults : IDisposable
{
    private readonly IDeviceEnumerator devices = (IDeviceEnumerator)Activator.CreateInstance(
        Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!)!;
    private IPolicyConfig? policy;
    private const int NotFound = unchecked((int)0x80070490);
    private static readonly PropertyKey ContainerKey = new(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    public Dictionary<AudioSlot, string?> ReadDefaults() => AudioDefaultPolicy.Slots.ToDictionary(s => s, Default);
    private string? Default(AudioSlot slot)
    {
        int result = devices.GetDefaultAudioEndpoint((int)slot.Flow, (int)slot.Role, out var endpoint);
        if (result == NotFound) return null;
        Marshal.ThrowExceptionForHR(result);
        try { Marshal.ThrowExceptionForHR(endpoint.GetId(out string id)); return id; }
        finally { Marshal.ReleaseComObject(endpoint); }
    }

    public List<AudioEndpoint> Endpoints()
    {
        Marshal.ThrowExceptionForHR(devices.EnumAudioEndpoints(2, 1, out var endpoints)); // all flows, active only
        try {
            Marshal.ThrowExceptionForHR(endpoints.GetCount(out uint count));
            var result = new List<AudioEndpoint>();
            for (uint i = 0; i < count; i++) {
                Marshal.ThrowExceptionForHR(endpoints.Item(i, out var endpoint));
                try {
                    Marshal.ThrowExceptionForHR(endpoint.GetId(out string id));
                    Guid container = Guid.Empty;
                    if (endpoint.OpenPropertyStore(0, out var store) >= 0) {
                        try {
                            var key = ContainerKey;
                            if (store.GetValue(ref key, out var value) >= 0) {
                                try { if (value.Type == 72 && value.Pointer != IntPtr.Zero) container = Marshal.PtrToStructure<Guid>(value.Pointer); }
                                finally { PropVariantClear(ref value); }
                            }
                        } finally { Marshal.ReleaseComObject(store); }
                    }
                    Marshal.ThrowExceptionForHR(((IEndpoint)endpoint).GetDataFlow(out int flow));
                    result.Add(new(id, container, true, (AudioFlow)flow));
                } finally { Marshal.ReleaseComObject(endpoint); }
            }
            return result;
        } finally { Marshal.ReleaseComObject(endpoints); }
    }

    public bool Restore(AudioRestore change)
    {
        // Recheck after planning: don't overwrite a user's intervening device choice.
        if (!string.Equals(Default(change.Slot), change.Expected, StringComparison.OrdinalIgnoreCase)) return false;
        int found = devices.GetDevice(change.Target, out var target);
        if (found < 0) return false;
        try { if (target.GetState(out uint state) < 0 || state != 1) return false; }
        finally { Marshal.ReleaseComObject(target); }
        policy ??= (IPolicyConfig)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), throwOnError: true)!)!;
        Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(change.Target, (int)change.Slot.Role));
        return true;
    }

    public void Dispose()
    {
        if (policy is not null) Marshal.ReleaseComObject(policy);
        Marshal.ReleaseComObject(devices);
    }

    // Windows Core Audio ABI. IPolicyConfig is an undocumented Windows interface;
    // its endpoint setter is best-effort and failures must retain the manual fallback.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out IDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice endpoint);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice endpoint);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpoint { [PreserveSig] int GetDataFlow(out int flow); }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id) { public readonly Guid Format = format; public readonly uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
