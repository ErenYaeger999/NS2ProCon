using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NS2Pro.Core;

namespace NS2Pro.App.Services;

// Optional, separately installed backend. No GPL library or executable is linked or bundled.
// Wire facts: hbashton/VIIPER VPCM V5, dualsensecombinedaudioduplexv5 (33-byte input).
public sealed class ViiperGamepad : IGameOutput
{
    private readonly object gate = new();
    private readonly int port, bus;
    private readonly TcpClient connection;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task reader, writer;
    private XboxReport latest;
    private long rumbleCommands, rumbleStops, nativeBlocks, nativeActiveBlocks;
    private volatile Exception? failure;
    private int disposed;
    public bool FeedbackAvailable => disposed == 0 && failure is null;
    public event Action<byte, byte>? RumbleChanged;
    public event Action<HapticFrame>? NativeHaptics;
    public string FeedbackSummary => $"本次增强反馈：普通指令 {Interlocked.Read(ref rumbleCommands)} 次 · 停止 {Interlocked.Read(ref rumbleStops)} 次 · 原生采样 {Interlocked.Read(ref nativeBlocks)} 块 · 非零 {Interlocked.Read(ref nativeActiveBlocks)} 块";

    private ViiperGamepad(int port, int bus, TcpClient connection)
    {
        this.port = port; this.bus = bus; this.connection = connection;
        reader = Task.Run(ReadAsync); writer = Task.Run(WriteAsync);
    }

    public static async Task<string> ProbeAsync(int port = 3242, CancellationToken token = default)
    {
        using var result = await CommandAsync(port, "ping", token);
        if (!result.RootElement.TryGetProperty("server", out var server) || server.GetString() != "VIIPER")
            throw new IOException("本地端口不是 VIIPER 后端");
        return "VIIPER " + (result.RootElement.TryGetProperty("version", out var version) ? version.GetString() : "版本未知");
    }

    public static async Task<ViiperGamepad> CreateAsync(int port, CancellationToken token)
    {
        await ProbeAsync(port, token);
        int bus = 0;
        TcpClient? stream = null;
        try {
            using (var created = await CommandAsync(port, "bus/create", token)) bus = created.RootElement.GetProperty("busId").GetInt32();
            if (bus <= 0) throw new IOException("触觉后端返回无效的设备总线");
            using var device = await CommandAsync(port, $"bus/{bus}/add {{\"type\":\"{DualSenseWire.DeviceType}\"}}", token);
            string? id = device.RootElement.GetProperty("devId").GetString();
            if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiDigit)) throw new IOException("触觉后端返回无效的设备编号");
            stream = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await stream.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            await stream.GetStream().WriteAsync(Encoding.UTF8.GetBytes($"bus/{bus}/{id}\0"), timeout.Token);
            return new(port, bus, stream);
        } catch {
            stream?.Dispose();
            if (bus > 0) { try { using var removed = await CommandAsync(port, $"bus/remove {bus}", CancellationToken.None); } catch (Exception) { } }
            throw;
        }
    }

    public void Update(XboxReport report)
    {
        if (failure is not null) throw new IOException("实验性触觉后端已断开，请断开后重试或切回 Xbox 兼容模式", failure);
        lock (gate) latest = report;
    }

    private async Task WriteAsync()
    {
        uint sequence = 0;
        try {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(8));
            do {
                XboxReport report; lock (gate) report = latest;
                var packet = DualSenseWire.Encode(1, sequence++, DualSenseWire.Input(report));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await connection.GetStream().WriteAsync(packet, timeout.Token);
            } while (await timer.WaitForNextTickAsync(cancellation.Token));
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }

    private async Task ReadAsync()
    {
        uint? previous = null;
        var processor = new DualSenseHaptics();
        long lastMedia = 0;
        bool hadAudio = false;
        try {
            var stream = connection.GetStream();
            while (!cancellation.IsCancellationRequested) {
                byte[] header = new byte[DualSenseWire.HeaderSize];
                await stream.ReadExactlyAsync(header, cancellation.Token);
                byte[] payload = new byte[DualSenseWire.PayloadLength(header)];
                await stream.ReadExactlyAsync(payload, cancellation.Token);
                byte type = DualSenseWire.Validate(header, payload, ref previous);
                // Recent V5 backends publish the same rear sample in a realtime lane and in
                // the atomic speaker generation. Consume only 0x83, never stretch it by replaying 0x84.
                if (type == 0x84 && payload.Length == DualSenseWire.FeedbackSize) continue;
                int offset;
                if (type == 0x81 && payload.Length == DualSenseWire.FeedbackSize) offset = 0;
                else if (type == 0x83 && payload.Length == 2 + DualSenseWire.FeedbackSize + 1920 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(payload) == DualSenseWire.FeedbackSize) offset = 2;
                else if (type == 0x85 && payload.Length == 9) continue;
                else throw new InvalidDataException("触觉后端的反馈格式不兼容；需要提供 VPCM V5 的 VIIPER 版本");
                if (type == 0x81) {
                    // V5 compact motors are cumulative caches, even after the game restores
                    // audio haptics. Only exact USB output commands carry current enables.
                    // Media snapshots must not renew a stopped compatible-rumble effect.
                    var command = payload.AsSpan(28, 48);
                    if (command[0] == 0x02 && payload[76] != 0x36) {
                        bool compatible = (command[1] & 0x01) != 0 || (command[39] & 0x04) != 0;
                        Interlocked.Increment(ref rumbleCommands);
                        if (!compatible || (command[3] == 0 && command[4] == 0)) Interlocked.Increment(ref rumbleStops);
                        RumbleChanged?.Invoke(compatible ? command[4] : (byte)0, compatible ? command[3] : (byte)0);
                    }
                    continue;
                }
                int carrier = offset + 76;
                if (payload[carrier] != 0x36 || payload[carrier + 76] != 0x92 || payload[carrier + 77] != 64)
                    throw new InvalidDataException("触觉后端没有有效的原生触觉采样");
                if (lastMedia != 0 && Stopwatch.GetElapsedTime(lastMedia).TotalMilliseconds > 100) { processor.Reset(); hadAudio = false; }
                lastMedia = Stopwatch.GetTimestamp();
                var frame = processor.Process(payload.AsSpan(carrier + 78, 64));
                Interlocked.Increment(ref nativeBlocks);
                if (!frame.IsSilent) Interlocked.Increment(ref nativeActiveBlocks);
                if (!frame.IsSilent || hadAudio) NativeHaptics?.Invoke(frame);
                hadAudio = !frame.IsSilent;
            }
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }

    private void Fail(Exception error)
    {
        if (cancellation.IsCancellationRequested) return;
        failure = error;
        try { NativeHaptics?.Invoke(default); RumbleChanged?.Invoke(0, 0); } catch (Exception) { }
        cancellation.Cancel(); connection.Dispose();
    }

    private static async Task<JsonDocument> CommandAsync(int port, string command, CancellationToken token)
    {
        if (port is < 1024 or > 65535) throw new ArgumentException("本地后端端口无效");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var client = new TcpClient { NoDelay = true };
        try {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\0"), timeout.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            byte[] buffer = new byte[2048];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0) {
                if (body.Length + read > 65536) throw new IOException("触觉后端响应过大");
                body.Write(buffer, 0, read);
            }
            var json = JsonDocument.Parse(body.ToArray());
            if (json.RootElement.TryGetProperty("status", out _)) {
                json.Dispose(); throw new IOException("触觉后端拒绝请求，请检查版本、localhost 访问及 USBIP 驱动");
            }
            return json;
        } catch (SocketException error) {
            throw new IOException("未找到本地 VIIPER。请先启动兼容的 V5 后端，或改回 Xbox 兼容模式。", error);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel(); connection.Dispose();
        try { Task.WhenAll(reader, writer).Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        try { using var removed = CommandAsync(port, $"bus/remove {bus}", CancellationToken.None).GetAwaiter().GetResult(); } catch (Exception) { }
        GC.KeepAlive(this);
    }
}
