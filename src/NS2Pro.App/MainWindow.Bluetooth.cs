using Windows.Devices.Bluetooth;
using NS2Pro.App.Services;

namespace NS2Pro.App;

public sealed partial class MainWindow
{
    private Task<BluetoothLEDevice?> OpenBluetoothDeviceAsync(ulong address, BluetoothAddressType type, CancellationToken token)
    {
        var completion = new TaskCompletionSource<BluetoothLEDevice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Device access can require Windows consent. Initiate it on the window thread.
        if (!DispatcherQueue.TryEnqueue(async () => {
            using var cancelled = token.Register(() => completion.TrySetCanceled(token));
            try {
                token.ThrowIfCancellationRequested();
                var operation = type == BluetoothAddressType.Unspecified ? BluetoothLEDevice.FromBluetoothAddressAsync(address) :
                    BluetoothLEDevice.FromBluetoothAddressAsync(address, type);
                // A late native result still needs disposing after cancellation/timeout.
                var device = await NativeOperation.WaitAsync(operation, token, releaseLate: value => value?.Dispose());
                if (!completion.TrySetResult(device)) device?.Dispose();
            } catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetException(new IOException("应用窗口已关闭，无法打开蓝牙设备。"));
        return completion.Task.WaitAsync(token);
    }
}
