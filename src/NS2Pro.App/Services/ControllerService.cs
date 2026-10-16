using NS2Pro.Core;
using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace NS2Pro.App.Services;

public enum ConnectionPhase { Idle, Preparing, Searching, Connecting, Connected, Stopping, Error }
public sealed record ConnectionInfo(ConnectionPhase Phase, string Message, string? Address = null, double Hertz = 0, long Reports = 0);

public sealed class ControllerService(AppSettings settings)
{
    public static readonly Guid InputUuid = new("7492866c-ec3e-4619-8258-32755ffcc0f8");
    private static readonly Guid ServiceUuid = new("ab7de9be-89fe-49ad-828f-118f09df7fd0");
    private static readonly Guid CommandUuid = new("649d4ac9-8eb7-4e6c-af44-1ea54fe5f005");
    private static readonly Guid ResponseUuid = new("c765a961-d9d8-4d36-a20a-5315b111836a");
    private CancellationTokenSource? cancellation;
    private Task? running;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private volatile ConnectionInfo info = new(ConnectionPhase.Idle, "准备好连接你的手柄");
    private volatile ControllerInput? input;
    public ConnectionInfo Info => info;
    public ControllerInput? Input => input;
    public event Action<ConnectionInfo>? Changed;

    private void Publish(ConnectionPhase phase, string message, string? address = null, double hertz = 0, long reports = 0)
    {
        info = new(phase, message, address, hertz, reports);
        Changed?.Invoke(info);
    }

    public async Task StartAsync()
    {
        await lifecycle.WaitAsync();
        try {
            if (running is { IsCompleted: false }) return;
            cancellation?.Dispose();
            cancellation = new();
            running = Task.Run(() => RunAsync(cancellation.Token));
        } finally { lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync();
        try {
            if (running is { IsCompleted: false }) {
                Publish(ConnectionPhase.Stopping, "正在安全断开连接…", info.Address);
                cancellation!.Cancel();
                await running;
            }
            input = null;
            Publish(ConnectionPhase.Idle, "PC 连接已结束，可以按 HOME 回到 NS2");
            AppLog.Write("info", "连接已停止，虚拟手柄已释放");
        } finally { lifecycle.Release(); }
    }

    public static async Task<string> CheckBluetoothAsync(bool enable = false)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync();
        if (adapter is null || !adapter.IsLowEnergySupported) throw new InvalidOperationException("电脑没有可用的蓝牙 LE 适配器");
        var radio = await adapter.GetRadioAsync();
        if (radio.State != RadioState.On && enable) {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed ||
                await radio.SetStateAsync(RadioState.On) != RadioAccessStatus.Allowed)
                throw new InvalidOperationException("请在 Windows 设置中开启蓝牙");
        }
        if (radio.State != RadioState.On) throw new InvalidOperationException("Windows 蓝牙已关闭");
        return "蓝牙 LE 已就绪";
    }

    private async Task RunAsync(CancellationToken token)
    {
        try {
            Publish(ConnectionPhase.Preparing, "正在检查蓝牙与虚拟手柄…");
            AppLog.Write("info", await CheckBluetoothAsync(true));
            // Verify the installed driver without retaining a virtual device while scanning.
            using (var check = new VirtualGamepad()) { }
            while (!token.IsCancellationRequested) {
                Publish(ConnectionPhase.Searching, "长按手柄顶部 SYNC，直到指示灯循环闪烁");
                var address = await WaitForControllerAsync(token);
                try { await ConnectAsync(address, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) {
                    AppLog.Write("error", error.Message);
                    Publish(ConnectionPhase.Error, error.Message, FormatAddress(address));
                    await Task.Delay(2500, token);
                }
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Write("error", error.ToString()); Publish(ConnectionPhase.Error, error.Message); }
        finally { input = null; }
    }

    private async Task<ulong> WaitForControllerAsync(CancellationToken token)
    {
        var ready = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        watcher.Received += (_, args) => {
            foreach (var manufacturer in args.Advertisement.ManufacturerData) {
                if (manufacturer.CompanyId != ControllerProtocol.ManufacturerId) continue;
                var decoded = ControllerProtocol.ParseAdvertisement(ReadBuffer(manufacturer.Data));
                if (decoded?.Kind != AdvertisementKind.Discovery) continue;
                var address = FormatAddress(args.BluetoothAddress);
                if (settings.LastControllerAddress.Length > 0 && !address.Equals(settings.LastControllerAddress, StringComparison.OrdinalIgnoreCase)) continue;
                ready.TrySetResult(args.BluetoothAddress);
            }
        };
        watcher.Stopped += (_, args) => {
            if (args.Error != BluetoothError.Success) ready.TrySetException(new InvalidOperationException($"蓝牙扫描已停止：{args.Error}"));
        };
        try { watcher.Start(); return await ready.Task.WaitAsync(token); }
        finally { watcher.Stop(); }
    }

    private async Task ConnectAsync(ulong address, CancellationToken token)
    {
        string mac = FormatAddress(address);
        Publish(ConnectionPhase.Connecting, "正在建立临时蓝牙连接…", mac);
        AppLog.Write("info", $"发现 SYNC 广播，连接 {mac}；保留原有主机配对");
        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask().WaitAsync(TimeSpan.FromSeconds(15), token)
            ?? throw new InvalidOperationException("无法打开蓝牙手柄，请再长按一次 SYNC");
        using var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
        if (session is not null) session.MaintainConnection = true;
        using var preference = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
        var discovery = await device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(20), token);
        if (discovery.Status != GattCommunicationStatus.Success || discovery.Services.Count == 0)
            throw new InvalidOperationException("未能读取手柄服务，请确认它处于 SYNC 模式");
        using var service = discovery.Services[0];
        var characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(15), token);
        if (characteristics.Status != GattCommunicationStatus.Success) throw new InvalidOperationException("无法读取手柄输入服务");
        GattCharacteristic Find(Guid id) => characteristics.Characteristics.FirstOrDefault(x => x.Uuid == id)
            ?? throw new InvalidOperationException($"手柄缺少所需蓝牙通道 {id}");
        var command = Find(CommandUuid);
        var response = Find(ResponseUuid);
        var reportCharacteristic = Find(InputUuid);
        var calibration = await ReadCalibrationsAsync(command, response, token);
        using var output = new VirtualGamepad();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.ConnectionStatusChanged += (_, _) => {
            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) disconnected.TrySetResult();
        };
        var clock = Stopwatch.StartNew();
        long count = 0;
        int seenRight=0, seenLeft=0, seenExtra=0;
        double leftTravel=0, rightTravel=0;
        long lastTicks = Stopwatch.GetTimestamp();
        bool accepting = true;
        var outputGate = new object();
        void Received(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            lock (outputGate) {
                if (!accepting) return;
                try {
                    var decoded = ControllerProtocol.Decode(ReadBuffer(args.CharacteristicValue), calibration[0], calibration[1], settings.Layout, settings.Deadzone);
                    output.Update(decoded.Xbox);
                    input = decoded;
                    Interlocked.Increment(ref count);
                    seenRight |= decoded.RightButtons; seenLeft |= decoded.LeftButtons; seenExtra |= decoded.ExtraButtons;
                    leftTravel=Math.Max(leftTravel,Math.Sqrt(decoded.UnfilteredLeft.X*decoded.UnfilteredLeft.X+decoded.UnfilteredLeft.Y*decoded.UnfilteredLeft.Y));
                    rightTravel=Math.Max(rightTravel,Math.Sqrt(decoded.UnfilteredRight.X*decoded.UnfilteredRight.X+decoded.UnfilteredRight.Y*decoded.UnfilteredRight.Y));
                    if(count==1)AppLog.Write("info","已收到首份输入报告，实时检测已开始");
                    Interlocked.Exchange(ref lastTicks, Stopwatch.GetTimestamp());
                } catch (Exception error) {
                    AppLog.Write("error", "输入输出失败：" + error.Message);
                    disconnected.TrySetException(error);
                }
            }
        }
        reportCharacteristic.ValueChanged += Received;
        try {
            var subscribed = await reportCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
            if (subscribed != GattCommunicationStatus.Success) throw new InvalidOperationException("无法订阅手柄输入");
            settings.LastControllerAddress = mac;
            settings.Save();
            AppLog.Write("info", "蓝牙与 Xbox 360 输出已连接，未写入配对资料");
            while (!token.IsCancellationRequested && !disconnected.Task.IsCompleted) {
                await Task.Delay(250, token);
                Publish(ConnectionPhase.Connected, "已连接到 PC，游戏可识别为 Xbox 360 手柄", mac, count / Math.Max(clock.Elapsed.TotalSeconds, .001), count);
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastTicks)).TotalSeconds > 1) {
                    lock (outputGate) { output.Update(default); input = null; }
                }
            }
            if (disconnected.Task.IsCompleted) await disconnected.Task;
        } finally {
            lock (outputGate) { accepting = false; output.Update(default); input = null; }
            reportCharacteristic.ValueChanged -= Received;
            try { await reportCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
            if (session is not null) session.MaintainConnection = false;
            AppLog.Write("info", "蓝牙连接已释放");
            AppLog.Write("info", $"本次输入：{count} 份 · {count/Math.Max(clock.Elapsed.TotalSeconds,.001):F1} Hz · R={seenRight:X2} L={seenLeft:X2} Extra={seenExtra:X2} · 摇杆最大行程 {leftTravel:P0}/{rightTravel:P0}");
        }
    }

    private static async Task<StickCalibration[]> ReadCalibrationsAsync(GattCharacteristic command, GattCharacteristic response, CancellationToken token)
    {
        TaskCompletionSource<byte[]>? pending = null;
        void Received(GattCharacteristic sender, GattValueChangedEventArgs args) {
            var data = ReadBuffer(args.CharacteristicValue);
            if (data.Length >= 8 && data[0] == 2 && data[1] == 1 && data[3] == 4) pending?.TrySetResult(data);
        }
        response.ValueChanged += Received;
        try {
            if (await response.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token) != GattCommunicationStatus.Success)
                throw new InvalidOperationException("无法读取摇杆校准数据");
            async Task<byte[]> Read(uint address) {
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var writer = new DataWriter();
                writer.WriteBytes(ControllerProtocol.CalibrationReadRequest(address));
                if (await command.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token) != GattCommunicationStatus.Success)
                    throw new IOException("手柄校准读取请求失败");
                return ControllerProtocol.ParseCalibrationResponse(await pending.Task.WaitAsync(TimeSpan.FromSeconds(4), token), address);
            }
            var values = new List<StickCalibration>();
            foreach (var (user, factory) in new (uint, uint)[] { (0x1fc042, 0x130a8), (0x1fc062, 0x130e8) }) {
                var calibration = ControllerProtocol.ParseCalibration(await Read(user)) ?? ControllerProtocol.ParseCalibration(await Read(factory));
                values.Add(calibration ?? throw new InvalidDataException("手柄没有有效的摇杆校准数据"));
            }
            return values.ToArray();
        } finally {
            response.ValueChanged -= Received;
            try { await response.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
        }
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }
    public static string FormatAddress(ulong address) => string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (i * 8)) & 255).ToString("X2")));
}
