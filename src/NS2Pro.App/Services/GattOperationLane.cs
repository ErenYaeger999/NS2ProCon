using NS2Pro.Core;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;

namespace NS2Pro.App.Services;

/// <summary>One lane per connection for CCCD/config writes; input notifications and
/// the independent rumble writer never wait for this lane.</summary>
internal sealed class GattOperationLane(InputStreamSession physical)
{
    private readonly StatefulOperationGate operations = new();
    public bool IsUsable => operations.IsUsable;
    public string? FailedStage => operations.FailedStage;
    public Task WhenSettled => operations.WhenSettled;

    public async Task<T> RunAsync<T>(string stage, Func<IAsyncOperation<T>> start, CancellationToken token, int seconds = 3, Action<T>? releaseLate = null)
    {
        IAsyncOperation<T>? pending = null;
        try {
            var result = await operations.RunAsync(stage, () => {
                if (physical.TransportLost) throw new ObjectDisposedException("蓝牙会话已结束");
                AppLog.Write("info", "GATT 操作：" + stage);
                pending = start(); return pending.AsTask();
            }, TimeSpan.FromSeconds(seconds), token, () => pending?.Cancel(), releaseLate);
            if (result is GattCommunicationStatus.Unreachable) physical.End("GATT 设备不可达 · " + stage, lost: true);
            return result;
        } catch (Exception error) when (error is ObjectDisposedException || error.HResult == unchecked((int)0x80000013)) {
            physical.End("GATT 会话已失效 · " + stage, lost: true, error: error); throw;
        }
    }
}
