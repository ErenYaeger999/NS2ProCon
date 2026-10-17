using System.Collections.Concurrent;
using System.Diagnostics;
using NS2Pro.Core;

internal static class HapticChecks
{
    internal static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("Haptics: " + name); passed++; }
        static (int Low, int High) ReadMotor(byte[] packet, int offset)
        {
            ulong word = 0;
            for (int i = 0; i < 5; i++) word |= (ulong)packet[offset + i] << (8 * i);
            return ((int)((word >> 10) & 1023), (int)((word >> 30) & 1023));
        }
        static bool Active(byte[] packet) => ReadMotor(packet, 2) != (0, 0) || ReadMotor(packet, 18) != (0, 0);
        var motor = new HapticMotor(1, .5, 160, 320);
        var packet = HapticProtocol.EncodeBluetooth(new(motor, default), 0x2f, 1);
        Check(packet.Length == 33 && packet[0] == 0 && packet[1] == 0x5f && packet[17] == 0x5f, "BLE framing and sequence wrap");
        Check(ReadMotor(packet, 2) == (500, 250) && ReadMotor(packet, 18) == (0, 0), "independent LRA bands and software ceiling");
        Check(packet.AsSpan(7, 10).ToArray().All(b => b == 0) && packet.AsSpan(23, 10).ToArray().All(b => b == 0), "unused operation slots are clear");
        Check(!Active(HapticProtocol.EncodeBluetooth(new(motor, motor), 0, 0)), "zero master strength silences every band");
        Check(!Active(HapticProtocol.EncodeBluetooth(new(new(double.NaN, double.PositiveInfinity), default), 0, 1)), "nonfinite amplitudes never energize a motor");
        Check(ReadMotor(HapticProtocol.EncodeBluetooth(new(new(-10, 20), default), 0, 20), 2) == (0, 500), "out-of-range values are bounded");
        Check(HapticProtocol.FromXbox(0, 0).IsSilent, "exact game stop");
        Check(HapticProtocol.FromXbox(1, 0).Left.LowAmplitude > 0 && HapticProtocol.FromXbox(0, 1).Left.HighAmplitude > 0, "weak feedback is retained independently");
        Check(HapticProtocol.FromXbox(255, 0).Left == HapticProtocol.FromXbox(255, 0).Right, "XInput bands are not mislabeled as spatial stereo");
        Check(HapticProtocol.TestFrame(HapticTest.Left, TimeSpan.Zero).Right.IsSilent, "left identification leaves right quiet");
        Check(HapticProtocol.TestFrame(HapticTest.Right, TimeSpan.Zero).Left.IsSilent, "right identification leaves left quiet");
        Check(HapticProtocol.TestFrame(HapticTest.Click, TimeSpan.FromMilliseconds(70)).IsSilent, "click has a short finite envelope");
        Check(HapticProtocol.TestFrame(HapticTest.Pulses, TimeSpan.FromMilliseconds(150)).IsSilent, "pulse silence is encoded");

        var records = new ConcurrentQueue<byte[]>();
        int inFlight = 0, maximumFlight = 0;
        var session = new HapticSession(async (data, token) => {
            int count = Interlocked.Increment(ref inFlight);
            maximumFlight = Math.Max(maximumFlight, count);
            try { await Task.Delay(3, token); records.Enqueue(data); }
            finally { Interlocked.Decrement(ref inFlight); }
        }, true, 1);
        try {
            await Until(() => records.Count > 0);
            Check(!Active(records.Last()), "a new connection begins with a stop");
            session.SubmitXbox(255, 255);
            await Task.Delay(50);
            Check(records.All(p => !Active(p)), "no output before physical input is available");
            session.SetInputAvailable(true); session.SubmitXbox(100, 200);
            await Until(() => records.Any(Active));
            int count = records.Count;
            await Task.Delay(120);
            Check(records.Count > count + 2 && Active(records.Last()), "held state refreshes without another game callback");
            for (int i = 1; i <= 1000; i++) session.SubmitXbox((byte)(i % 255 + 1), 45);
            session.SubmitXbox(12, 34);
            await Until(() => session.Status.Writes > count + 5);
            Check(session.Status.Coalesced > 900 && maximumFlight == 1, "bursts coalesce with one writer");
            Check(ReadMotor(records.Last(), 2) == ReadMotor(HapticProtocol.EncodeBluetooth(HapticProtocol.FromXbox(12, 34), 0, 1), 2), "latest state wins instead of replaying a backlog");
            session.Configure(false, 1);
            await Until(() => !Active(records.Last()));
            session.Configure(true, 1); await Task.Delay(45);
            Check(!Active(records.Last()), "enabling cannot replay a disabled effect");
            session.SubmitXbox(255, 255); await Until(() => Active(records.Last()));
            session.SetInputAvailable(false); await Until(() => !Active(records.Last()));
            session.SetInputAvailable(true); await Task.Delay(45);
            Check(!Active(records.Last()), "input resume cannot resurrect stale feedback");
            Check(session.BeginTest(HapticTest.Left), "connected test begins");
            await Until(() => Active(records.Last()));
            Check(ReadMotor(records.Last(), 18) == (0, 0), "test selects one physical side");
            session.SubmitXbox(255, 255);
            await Until(() => !session.Status.Testing, 2000);
            await Until(() => !Active(records.Last()));
            Check(!Active(records.Last()), "automatic test stop drops game feedback received during testing");
            session.BeginTest(HapticTest.Pulses); await Until(() => Active(records.Last()));
            session.Stop(); await Until(() => !Active(records.Last()));
            Check(!session.Status.Testing, "manual stop cancels a running pattern");
        } finally { await session.DisposeAsync(); }
        Check(!Active(records.Last()) && !session.Status.Ready, "dispose sends final silence and retires the session");
        int retired = records.Count;
        session.SubmitXbox(255, 255); await Task.Delay(40);
        Check(records.Count == retired, "late callbacks cannot restart a retired session");

        var ordered = new ConcurrentQueue<byte[]>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool block = true;
        await using (var priority = new HapticSession(async (data, token) => {
            ordered.Enqueue(data);
            if (Active(data) && block) { block = false; entered.TrySetResult(); await release.Task.WaitAsync(token); }
        }, true, 1)) {
            priority.SetInputAvailable(true); priority.SubmitXbox(230, 0);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            int firstActive = ordered.Count - 1;
            priority.SubmitXbox(0, 0); priority.SubmitXbox(12, 34); release.TrySetResult();
            await Until(() => ordered.Count > firstActive + 2);
            Check(!Active(ordered.ToArray()[firstActive + 1]), "stop barrier cannot be overwritten by a newer active state");
            priority.Stop(); await Until(() => !Active(ordered.Last()));
            priority.SubmitRich(new(new(.5, 0), default));
            await Until(() => Active(ordered.Last()));
            priority.SubmitXbox(0, 0);
            await Task.Delay(25);
            Check(Active(ordered.Last()) && ReadMotor(ordered.Last(), 18) == (0, 0), "ordinary zero feedback does not mask an authored stereo sample");
            await Until(() => !Active(ordered.Last()));
            Check(true, "native sample lease expires to silence when the source disappears");
        }

        var expiredWrites = new ConcurrentQueue<byte[]>();
        var richEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRich = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var expiring = new HapticSession(async (data, token) => {
            expiredWrites.Enqueue(data);
            if (Active(data)) { richEntered.TrySetResult(); await releaseRich.Task.WaitAsync(token); }
        }, true, 1)) {
            expiring.SetInputAvailable(true);
            await Until(() => !expiredWrites.IsEmpty);
            await Task.Delay(40); // let the initial stop and wake drain before this isolated sample
            expiring.SubmitRich(new(new(.5, 0), default));
            await richEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(130); // the sample expires while its final BLE write is in flight
            releaseRich.TrySetResult();
            bool silenced = true;
            try { await Until(() => !Active(expiredWrites.Last()), 500); }
            catch (TimeoutException) { silenced = false; }
            Check(silenced, "native lease expiring during a BLE write still emits a final stop without another callback");
        }

        var timed = Stopwatch.StartNew();
        await using (var timeout = new HapticSession(async (_, token) => await Task.Delay(Timeout.Infinite, token), true, 1)) {
            await Until(() => timeout.Status.Faulted, 2200);
        }
        Check(timed.ElapsedMilliseconds < 3000, "blocked BLE writes and cleanup have bounded deadlines");

        int failures = 0;
        await using (var broken = new HapticSession((_, _) => { failures++; throw new IOException("injected write failure"); }, true, 1)) {
            await Until(() => broken.Status.Faulted);
            broken.SetInputAvailable(true); broken.SubmitXbox(255, 255);
            Check(!broken.Status.Ready && failures <= 2, "write failure is isolated with bounded stop attempt");
        }
        var malformed = NS2Pro.App.Services.AppSettings.FromJson("{\"RumbleStrength\":9,\"RumbleEnabled\":false}");
        Check(malformed.RumbleStrength == 1 && !malformed.RumbleEnabled, "settings normalize without enabling a disabled feature");
        Check(NS2Pro.App.Services.AppSettings.FromJson("{}").RumbleEnabled && NS2Pro.App.Services.AppSettings.FromJson("{}").RumbleStrength == .65, "old settings acquire conservative defaults");
        return passed;
    }

    internal static async Task Until(Func<bool> condition, int milliseconds = 2500)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) {
            if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException("Haptic check timed out");
            await Task.Delay(5);
        }
    }
}
