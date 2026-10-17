using System.Diagnostics;
using System.Runtime.InteropServices;
using NS2Pro.App.Services;
using NS2Pro.Core;

// Opt-in integration check: creates only its own virtual pad and never opens a physical radio.
internal static class FeedbackProbe
{
    public static async Task RunAsync()
    {
        using var pad = new VirtualGamepad();
        if (!pad.FeedbackAvailable) throw new Exception("ViGEm notification registration failed");
        uint? slot = null;
        // Confirm the actual XInput slot by an input signature. A cached driver index can be stale
        // while Windows/Steam are enumerating, and must never direct rumble to somebody else's pad.
        var marker = new XboxReport { LeftX = 127, LeftY = -251, RightX = 193, RightY = -389 };
        pad.Update(marker);
        await HapticChecks.Until(() => {
            for (uint index = 0; index < 4; index++) {
                if (XInputGetState(index, out var state) == 0 && state.Report.LeftX == marker.LeftX &&
                    state.Report.LeftY == marker.LeftY && state.Report.RightX == marker.RightX && state.Report.RightY == marker.RightY) {
                    slot = index; return true;
                }
            }
            return false;
        }, 5000);
        int received = 0;
        byte wantedLow = 0, wantedHigh = 0;
        var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pad.RumbleChanged += (low, high) => {
            Console.WriteLine($"Feedback slot {slot}: {low}/{high}; expected {wantedLow}/{wantedHigh}");
            Interlocked.Increment(ref received);
            if (low == wantedLow && high == wantedHigh) confirmed.TrySetResult();
        };
        var clock = Stopwatch.StartNew();
        try {
            foreach (var (low, high) in new (byte, byte)[] { (52, 171), (1, 0), (0, 1), (255, 255), (0, 0) }) {
                wantedLow = low; wantedHigh = high;
                confirmed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                GC.Collect(); GC.WaitForPendingFinalizers();
                var vibration = new Vibration { Low = (ushort)(low * 257), High = (ushort)(high * 257) };
                if (XInputSetState(slot!.Value, ref vibration) != 0) throw new Exception("XInput could not reach the probe's own virtual pad");
                try { await confirmed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { throw new TimeoutException($"No matching feedback for {low}/{high}; callbacks={received}; slot={slot}"); }
            }
            // A subscriber failure must not cross the unmanaged callback boundary.
            pad.RumbleChanged += (_, _) => throw new InvalidOperationException("injected subscriber failure");
            var pulse = new Vibration { Low = 12000 };
            _ = XInputSetState(slot!.Value, ref pulse);
            await Task.Delay(100);
            Console.WriteLine($"PASS: real XInput -> ViGEm callbacks, both bands, zero, weak feedback, GC lifetime and exception boundary ({received} callbacks, {clock.ElapsedMilliseconds} ms). No physical controller used.");
        } finally {
            if (slot.HasValue) { var zero = default(Vibration); _ = XInputSetState(slot.Value, ref zero); }
        }
        pad.Dispose(); pad.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Vibration { public ushort Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct State { public uint Packet; public XboxReport Report; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] private static extern uint XInputSetState(uint index, ref Vibration vibration);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern uint XInputGetState(uint index, out State state);
}
