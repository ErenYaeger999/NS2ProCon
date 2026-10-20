using System.Diagnostics;
using NS2Pro.Core;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace NS2Pro.App.Services;

/// <summary>Optional FD2 subscription; failure leaves the existing button/rumble path usable.</summary>
internal sealed class BluetoothMotion : IAsyncDisposable
{
    private readonly GattCharacteristic input, proInput, command, response;
    private readonly MotionSession samples;
    private readonly ViiperGamepad output;
    private readonly Func<bool> canReceive;
    private readonly Action<byte[], long> commonInput;
    private readonly TaskCompletionSource firstData = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new(), ackGate = new();
    private TaskCompletionSource<byte[]>? pending;
    private byte expectedCommand, expectedSubcommand;
    private bool accepting, enableAttempted, firstSample = true, firstNotification = true;
    private bool commonPrimary;
    private bool powerSnapshot;
    private int disposed;

    public bool CommonInputIsPrimary => Volatile.Read(ref commonPrimary);
    public bool NativePowerSnapshotActive => Volatile.Read(ref powerSnapshot);

    private BluetoothMotion(GattCharacteristic input, GattCharacteristic proInput, GattCharacteristic command, GattCharacteristic response,
        MotionSession samples, ViiperGamepad output, Func<bool> canReceive, Action<byte[], long> commonInput)
    {
        this.input = input; this.proInput = proInput; this.command = command; this.response = response;
        this.samples = samples; this.output = output; this.canReceive = canReceive;
        this.commonInput = commonInput;
    }

    public static async Task<BluetoothMotion> StartAsync(GattCharacteristic input, GattCharacteristic proInput, GattCharacteristic command,
        GattCharacteristic response, MotionSession samples, ViiperGamepad output, Func<bool> canReceive,
        Action<byte[], long> commonInput, Action<bool> commonModeChanged, CancellationToken token)
    {
        var connection = new BluetoothMotion(input, proInput, command, response, samples, output, canReceive, commonInput);
        try {
            connection.accepting = true;
            input.ValueChanged += connection.Received;
            response.ValueChanged += connection.Acknowledged;
            await SubscribeAsync(response, token);
            await connection.WriteFeatureAsync(2, token);
            connection.enableAttempted = true;
            await connection.WriteFeatureAsync(4, token);
            // Firmware may not route common reports while native 0x09 notifications are active.
            // A successful CCCD/ACK is not evidence that any IMU data has actually arrived.
            await SubscribeAsync(input, token);
            if (!await connection.WaitForDataAsync(600, token)) {
                AppLog.Write("info", "未收到并行体感通知，切换为公共报告同时接收按键与六轴");
                if (await NativeOperation.WaitAsync(proInput.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None), token, 3) != GattCommunicationStatus.Success)
                    throw new IOException("无法切换公共输入通道");
                commonModeChanged(true);
                Volatile.Write(ref connection.commonPrimary, true);
                await connection.WriteCommandAsync(MotionProtocol.SelectReportRequest(5), token);
                await SubscribeAsync(input, token);
                if (!await connection.WaitForDataAsync(600, token)) {
                    AppLog.Write("info", "正在启动公共报告传输，未写入主机地址或配对资料");
                    await connection.WriteCommandAsync(MotionProtocol.StartPublicReportsRequest(), token);
                    await connection.WriteFeatureAsync(2, token);
                    await connection.WriteFeatureAsync(4, token);
                    await connection.WriteCommandAsync(MotionProtocol.SelectReportRequest(5), token);
                    await SubscribeAsync(input, token);
                    if (!await connection.WaitForDataAsync(1500, token)) {
                        var read = await NativeOperation.WaitAsync(input.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), token, 2);
                        AppLog.Write("warn", $"公共体感首帧超时 · 直接读取 {read.Status} · {read.Value?.Length ?? 0} 字节");
                        throw new IOException("已订阅但没有收到有效体感首帧");
                    }
                }
            }
            AppLog.Write("info", connection.CommonInputIsPrimary ? "六轴已收到真实数据 · 公共报告承载按键与体感 · 定期订阅原报告刷新电量" :
                "六轴已收到真实数据 · 与原按键通道并行");
            return connection;
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            await connection.DisposeAsync(); throw;
        } catch (Exception error) {
            await connection.DisposeAsync();
            if (connection.CommonInputIsPrimary) {
                commonModeChanged(false);
                await SubscribeAsync(proInput, token);
                Volatile.Write(ref connection.commonPrimary, false);
                AppLog.Write("info", "体感未就绪，已恢复原有按键输入通道");
            }
            samples.Unavailable("体感初始化未完成，按键与震动可继续使用。请断开后重试。");
            AppLog.Write("warn", "可选体感初始化失败：" + error.Message);
            return connection;
        }
    }

    private static async Task SubscribeAsync(GattCharacteristic characteristic, CancellationToken token)
    {
        if (await NativeOperation.WaitAsync(characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify), token, 3) != GattCommunicationStatus.Success)
            throw new IOException("无法订阅体感蓝牙通道");
    }

    private async Task<bool> WaitForDataAsync(int milliseconds, CancellationToken token)
    {
        try { await firstData.Task.WaitAsync(TimeSpan.FromMilliseconds(milliseconds), token); return true; }
        catch (TimeoutException) { return false; }
    }

    private Task WriteFeatureAsync(byte subcommand, CancellationToken token) => WriteCommandAsync(MotionProtocol.FeatureRequest(subcommand), token);

    private async Task WriteCommandAsync(byte[] request, CancellationToken token)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (ackGate) { expectedCommand = request[0]; expectedSubcommand = request[3]; pending = completion; }
        try {
            using var writer = new DataWriter(); writer.WriteBytes(request);
            if (await NativeOperation.WaitAsync(command.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse), token, 2) != GattCommunicationStatus.Success)
                throw new IOException("体感启用指令写入失败");
            var ack = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
            if (request[0] == 0x0c && (ack.Length < 12 || ack.AsSpan(8, 4).ContainsAnyExcept((byte)0)))
                throw new IOException("手柄未确认体感配置");
            AppLog.Write("info", $"体感指令 {request[0]:X2}/{request[3]:X2} 已确认 · {Convert.ToHexString(ack)}");
        } finally { lock (ackGate) { if (ReferenceEquals(pending, completion)) pending = null; } }
    }

    private void Acknowledged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try {
            var data = Buffer(args);
            lock (ackGate) {
                if (data.Length >= 8 && data[0] == expectedCommand && data[1] == 1 && data[3] == expectedSubcommand)
                    pending?.TrySetResult(data);
            }
        } catch (Exception error) { AppLog.Write("warn", "体感配置响应无效：" + error.Message); }
    }

    private void Received(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        long at = Stopwatch.GetTimestamp();
        lock (gate) {
            if (!accepting) return;
            try {
                var packet = Buffer(args);
                if (firstNotification) {
                    firstNotification = false;
                    AppLog.Write("info", $"首份 FD2 通知：{packet.Length} 字节 · IMU {(packet.Length >= 60 ? Convert.ToHexString(packet.AsSpan(42, 18)) : "不完整")}");
                }
                if (CommonInputIsPrimary && packet.Length >= 16) {
                    commonInput(packet, at);
                }
                if (!canReceive()) return;
                if (!samples.Submit(packet, at)) return;
                output.UpdateMotion(samples.Output);
                firstData.TrySetResult();
                if (firstSample) {
                    firstSample = false;
                    var value = MotionProtocol.Decode(packet)!;
                    AppLog.Write("info", $"首份体感：{packet.Length} 字节 · 序号 {value.Sequence} · 时间戳 {value.DeviceTimestamp} · 加速度 {value.Acceleration} · 角速度 {value.Gyroscope}");
                }
            } catch (Exception error) {
                // FD2 may also own buttons. One malformed sensor notification must not
                // permanently turn off the shared input callback; newer valid data may recover.
                samples.Unavailable("体感样本读取失败，正在等待后续数据…");
                output.UpdateMotion(null); AppLog.Write("warn", "体感读取失败：" + error.Message);
            }
        }
    }

    private static byte[] Buffer(GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue.Length > 512) throw new InvalidDataException("体感报告过大");
        using var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var data = new byte[args.CharacteristicValue.Length]; reader.ReadBytes(data); return data;
    }

    // Native 0x09 is notify-only on this firmware. A read request returns ProtocolError.
    // Briefly lease its notifications for a real power snapshot; its controls remain usable
    // during the lease. Never infer a percentage from the common report's battery voltage.
    public async Task RefreshPowerAsync(CancellationToken token)
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void PowerReceived(GattCharacteristic sender, GattValueChangedEventArgs args) {
            if (args.CharacteristicValue.Length >= 11) received.TrySetResult();
        }
        proInput.ValueChanged += PowerReceived;
        Volatile.Write(ref powerSnapshot, true);
        try {
            await SubscribeAsync(proInput, token);
            await received.Task.WaitAsync(TimeSpan.FromMilliseconds(300), token);
        } finally {
            try {
                // Cleanup is independent of caller cancellation, including partially completed subscriptions.
                var restored = await NativeOperation.WaitAsync(proInput.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None), CancellationToken.None, 2);
                if (restored != GattCommunicationStatus.Success) throw new IOException("电量刷新后无法恢复公共输入通道，请重新连接。");
            } finally {
                Volatile.Write(ref powerSnapshot, false);
                proInput.ValueChanged -= PowerReceived;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lock (gate) { accepting = false; samples.Clear(); output.UpdateMotion(null); }
        input.ValueChanged -= Received;
        if (enableAttempted) {
            try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await WriteFeatureAsync(5, stop.Token); }
            catch (Exception error) { AppLog.Write("warn", "体感关闭确认未完成：" + error.Message); }
        }
        if (CommonInputIsPrimary) {
            try { using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await WriteCommandAsync(MotionProtocol.SelectReportRequest(9), restore.Token); }
            catch (Exception error) { AppLog.Write("warn", "恢复默认报告的确认未完成：" + error.Message); }
        }
        response.ValueChanged -= Acknowledged;
        foreach (var characteristic in new[] { input, response }) {
            try { await NativeOperation.WaitAsync(characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None), CancellationToken.None, 2); }
            catch (Exception) { }
        }
    }
}
