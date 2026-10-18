using NS2Pro.Core;

internal static class AudioDefaultChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool value, string text) { if (!value) throw new Exception("Audio defaults: " + text); passed++; }
        var original = AudioDefaultPolicy.Slots.ToDictionary(s => s, s => (string?)$"original-{s.Flow}-{s.Role}");
        var policy = new AudioDefaultPolicy(original);
        var current = new Dictionary<AudioSlot, string?>(original);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "controller-out", "controller-in" };
        var active = original.Values.Select(s => s!).Concat(owned).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Check(policy.Reconcile(current, owned, active).Count == 0, "unchanged system produces no writes");
        foreach (var slot in AudioDefaultPolicy.Slots) current[slot] = slot.Flow == AudioFlow.Render ? "controller-out" : "controller-in";
        var changes = policy.Reconcile(current, owned, active);
        Check(changes.Count == 6 && changes.All(c => c.Target == original[c.Slot]), "all six render/capture roles retain their distinct original devices");
        var multimedia = new AudioSlot(AudioFlow.Render, AudioRole.Multimedia);
        current[multimedia] = "new-headphones"; active.Add("new-headphones");
        Check(policy.Reconcile(current, owned, active).All(c => c.Slot != multimedia), "a user headphone change is left untouched");
        current[multimedia] = "controller-out";
        Check(policy.Reconcile(current, owned, active).Single(c => c.Slot == multimedia).Target == "new-headphones", "later virtual takeover restores the user's latest headphone choice");
        var capture = new AudioSlot(AudioFlow.Capture, AudioRole.Communications);
        active.Remove(original[capture]!);
        Check(policy.Reconcile(current, owned, active).All(c => c.Slot != capture), "unplugged microphones are never restored");
        current[capture] = null;
        Check(policy.Reconcile(current, owned, active).All(c => c.Slot != capture), "missing default is not replaced with an arbitrary device");
        current[multimedia] = "another-dualsense"; active.Add("another-dualsense");
        Check(policy.Reconcile(current, owned, active).All(c => c.Slot != multimedia), "another software's or physical DualSense is not an owned endpoint");
        var noDefaults = AudioDefaultPolicy.Slots.ToDictionary(s => s, _ => (string?)null);
        Check(new AudioDefaultPolicy(noDefaults).Reconcile(current, owned, active).Count == 0, "a session with no original device cannot invent a fallback");
        var alreadyVirtual = AudioDefaultPolicy.Slots.ToDictionary(s => s, _ => (string?)"controller-out");
        Check(new AudioDefaultPolicy(alreadyVirtual).Reconcile(current, owned, active).Count == 0, "an already wrong virtual default is not restored onto itself");
        current = new(original);
        Check(policy.Reconcile(current, owned, active).Count == 0, "disconnect with valid physical defaults causes no extra switching");
        var pendingPolicy = new AudioDefaultPolicy(original);
        current[multimedia] = "still-enumerating";
        pendingPolicy.Reconcile(current, owned, active);
        current[multimedia] = "controller-out";
        Check(pendingPolicy.Reconcile(current, owned, active).Single(c => c.Slot == multimedia).Target == original[multimedia],
            "an unclassified new default cannot overwrite the remembered physical device");
        var attempt = new ConnectionAttempt { Stage = "读取手柄服务" };
        Check(attempt.Describe(new IOException("DeviceUnreachable")).Contains("读取手柄服务：DeviceUnreachable"), "connection failure retains its precise stage and native reason");
        Check(attempt.Describe(new TimeoutException()).Contains("超时"), "native timeouts have actionable wording");
        Check(attempt.Describe(new UnauthorizedAccessException()).Contains("Windows 拒绝访问"), "access denial is distinct from a radio timeout");
        Check(attempt.Describe(new OperationCanceledException()).Contains("超时"), "an internal deadline is explained; caller cancellation is handled before formatting");
        Check(!ConnectionAttempt.CanUseAdvertisement(123, -127, false), "Windows out-of-range cached advertisements never start a connection");
        Check(!ConnectionAttempt.CanUseAdvertisement(0, -40, false), "a missing peer address cannot be opened");
        Check(!ConnectionAttempt.CanUseAdvertisement(123, -40, true), "anonymous advertisement cannot be addressed");
        Check(ConnectionAttempt.CanUseAdvertisement(123, -110, false), "weak but real advertisements are retained without an arbitrary signal cutoff");
        return passed;
    }
}
