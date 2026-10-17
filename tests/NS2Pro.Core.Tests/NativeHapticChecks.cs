using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NS2Pro.App.Services;
using NS2Pro.Core;

internal static class NativeHapticChecks
{
    public static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string description) { if (!value) throw new Exception("Native haptics: " + description); passed++; }
        static bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
        var encoded = DualSenseWire.Input(new XboxReport { Buttons = 0x1000 | 0x200 | 1, LeftX = 32767, LeftY = 32767, LeftTrigger = 255 });
        Check(encoded.Length == 33 && encoded[0] == 127 && encoded[1] == 129, "signed sticks and vertical orientation");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)) == (0x20 | 0x200 | 0x400) && encoded[8] == 1 && encoded[9] == 255, "Cross, R1, L2 and dpad use the selected XInput mapping");
        var packet = DualSenseWire.Encode(0x81, uint.MaxValue, new byte[474]);
        uint? previous = null;
        Check(DualSenseWire.Validate(packet.AsSpan(0, 16), packet.AsSpan(16), ref previous) == 0x81, "valid V5 frame");
        var wrapped = DualSenseWire.Encode(0x81, 0, new byte[474]);
        Check(DualSenseWire.Validate(wrapped.AsSpan(0, 16), wrapped.AsSpan(16), ref previous) == 0x81, "wire sequence wraps");
        wrapped[^1] ^= 1;
        Check(Reject(() => { uint? p = null; DualSenseWire.Validate(wrapped.AsSpan(0, 16), wrapped.AsSpan(16), ref p); }), "corrupt payload fails closed");
        packet[4] = 4;
        Check(Reject(() => DualSenseWire.PayloadLength(packet.AsSpan(0, 16))), "older wire versions cannot be silently misparsed");
        packet[4] = 5; packet[6] = 255; packet[7] = 255;
        Check(Reject(() => DualSenseWire.PayloadLength(packet.AsSpan(0, 16))), "unbounded frame allocation is rejected");
        var gap = DualSenseWire.Encode(0x81, 5, new byte[474]);
        Check(Reject(() => { uint? p = 1; DualSenseWire.Validate(gap.AsSpan(0, 16), gap.AsSpan(16), ref p); }), "feedback sequence gaps are explicit");

        var processor = new DualSenseHaptics();
        var tone = new byte[128];
        for (int i = 0; i < 64; i++) {
            tone[2 * i] = unchecked((byte)(sbyte)(60 * Math.Sin(2 * Math.PI * 160 * i / 3000)));
            tone[2 * i + 1] = unchecked((byte)(sbyte)(60 * Math.Sin(2 * Math.PI * 400 * i / 3000)));
        }
        processor.Process(tone.AsSpan(0, 64));
        var frame = processor.Process(tone.AsSpan(64, 64));
        Check(frame.Left.LowFrequency == 160 && frame.Right.HighFrequency == 400, "stereo native samples retain distinct frequency content");
        Check(frame.Left.LowAmplitude > .3 && frame.Right.HighAmplitude > .3 && frame.Left.HighAmplitude < .1 && frame.Right.LowAmplitude < .1, "bands stay distinct with bounded gain");
        processor.Reset();
        Check(processor.Process(new byte[64]).IsSilent, "new audio epochs contain no stale samples");
        processor.Process(Enumerable.Repeat((byte)127, 64).ToArray());
        Check(processor.Process(Enumerable.Repeat((byte)127, 64).ToArray()).IsSilent, "DC bias is not converted to vibration");
        Check(Reject(() => processor.Process(new byte[63])), "partial audio samples are rejected");
        var invalid = AppSettings.FromJson("{\"OutputMode\":99,\"ViiperPort\":1}");
        Check(invalid.OutputMode == GameOutputMode.Xbox && invalid.ViiperPort == 1024, "malformed settings cannot enable the experimental output");

        var requests = new ConcurrentQueue<string>();
        var allowFeedback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropStream = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var handlers = new ConcurrentBag<Task>();
        var server = Task.Run(async () => {
            try {
                while (!stop.IsCancellationRequested) {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    handlers.Add(Handle(client));
                }
            } catch (OperationCanceledException) { }
        });
        async Task Handle(TcpClient client)
        {
            using (client) {
                var stream = client.GetStream();
                var name = new List<byte>();
                var one = new byte[1];
                while (true) {
                    await stream.ReadExactlyAsync(one, stop.Token);
                    if (one[0] == 0) break;
                    if (name.Count > 500) throw new Exception("Bad test request");
                    name.Add(one[0]);
                }
                string command = Encoding.UTF8.GetString(name.ToArray()); requests.Enqueue(command);
                string answer = command switch {
                    "ping" => "{\"server\":\"VIIPER\",\"version\":\"test-v5\"}",
                    "bus/create" => "{\"busId\":47}",
                    "bus/47/remove 1" or "bus/remove 47" => "{}",
                    _ => "{\"busId\":47,\"devId\":\"1\"}"
                };
                if (command != "bus/47/1") { await stream.WriteAsync(Encoding.UTF8.GetBytes(answer), stop.Token); return; }
                uint? last = null;
                while (true) {
                    var head = new byte[16]; await stream.ReadExactlyAsync(head, stop.Token);
                    var body = new byte[DualSenseWire.PayloadLength(head)]; await stream.ReadExactlyAsync(body, stop.Token);
                    DualSenseWire.Validate(head, body, ref last);
                    if (body.Length == 33 && body[0] == 127 && BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)) == 0x20) { validInput.TrySetResult(); break; }
                }
                await allowFeedback.Task.WaitAsync(stop.Token);
                byte[] feedback = new byte[474]; feedback[0] = 57; feedback[1] = 23;
                var first = DualSenseWire.Encode(0x81, 10, feedback);
                await stream.WriteAsync(first.AsMemory(0, 3), stop.Token); // fragmented TCP header
                await stream.WriteAsync(first.AsMemory(3), stop.Token);
                for (int i = 0; i < 2; i++) {
                    var media = new byte[2 + 474 + 1920];
                    BinaryPrimitives.WriteUInt16LittleEndian(media, 474);
                    feedback.CopyTo(media, 2);
                    int carrier = 2 + 76;
                    media[carrier] = 0x36; media[carrier + 76] = 0x92; media[carrier + 77] = 64;
                    tone.AsSpan(i * 64, 64).CopyTo(media.AsSpan(carrier + 78, 64));
                    await stream.WriteAsync(DualSenseWire.Encode(0x84, (uint)(11 + i * 2), media.AsSpan(2, 474)), stop.Token);
                    await stream.WriteAsync(DualSenseWire.Encode(0x83, (uint)(12 + i * 2), media), stop.Token);
                }
                await dropStream.Task.WaitAsync(stop.Token);
            }
        }
        try {
            Check((await ViiperGamepad.ProbeAsync(port)).Contains("test-v5"), "non-mutating backend identity probe");
            using (var output = await ViiperGamepad.CreateAsync(port, CancellationToken.None)) {
                int richFrames = 0; bool ordinary = false;
                output.RumbleChanged += (low, high) => { if (low == 23 && high == 57) ordinary = true; };
                output.NativeHaptics += _ => Interlocked.Increment(ref richFrames);
                output.Update(new() { Buttons = 0x1000, LeftX = 32767 });
                await validInput.Task.WaitAsync(TimeSpan.FromSeconds(3));
                allowFeedback.TrySetResult();
                await HapticChecks.Until(() => richFrames == 2 && ordinary);
                Check(true, "loopback V5 input, fragmented feedback, ordinary fallback and stereo haptics without duplicated realtime samples");
                dropStream.TrySetResult();
                await HapticChecks.Until(() => !output.FeedbackAvailable);
                bool failed = false;
                try { output.Update(default); } catch (IOException) { failed = true; }
                Check(failed, "backend loss is explicit rather than a silent controller change");
                await Task.Run(() => {
                    SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());
                    output.Dispose();
                }).WaitAsync(TimeSpan.FromSeconds(6));
                Check(true, "cleanup cannot deadlock a UI thread with a non-pumping synchronization context");
            }
            Check(requests.Contains("bus/remove 47") && requests.All(r => !r.Contains("remove 1") && !r.Contains("remove 17")), "cleanup touches only the bus created by this connection");
            Check(requests.Any(r => r.Contains(DualSenseWire.DeviceType)), "audio-capable persona is requested before stream setup");
        } finally {
            stop.Cancel(); listener.Stop(); await server;
            try { await Task.WhenAll(handlers); } catch (OperationCanceledException) { }
        }
        return passed;
    }
    private sealed class NonPumpingContext : SynchronizationContext { public override void Post(SendOrPostCallback callback, object? state) { } }
}
