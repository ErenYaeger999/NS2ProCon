namespace NS2Pro.Core;

public enum AudioFlow { Render, Capture }
public enum AudioRole { Console, Multimedia, Communications }
public readonly record struct AudioSlot(AudioFlow Flow, AudioRole Role);
public sealed record AudioEndpoint(string Id, Guid Container, bool Active, AudioFlow Flow);
public sealed record AudioRestore(AudioSlot Slot, string Expected, string Target);

/// <summary>Remember each role independently; restore only the protected virtual endpoint.</summary>
public sealed class AudioDefaultPolicy(IReadOnlyDictionary<AudioSlot, string?> original)
{
    private readonly Dictionary<AudioSlot, string?> preferred = new(original);
    public static IEnumerable<AudioSlot> Slots => Enum.GetValues<AudioFlow>()
        .SelectMany(flow => Enum.GetValues<AudioRole>().Select(role => new AudioSlot(flow, role)));

    public IReadOnlyList<AudioRestore> Reconcile(IReadOnlyDictionary<AudioSlot, string?> current,
        IReadOnlySet<string> owned, IReadOnlySet<string> active)
    {
        var changes = new List<AudioRestore>();
        foreach (var slot in Slots) {
            string? now = current.GetValueOrDefault(slot);
            if (now is null) continue;
            if (!owned.Contains(now)) {
                // A different headset, microphone or audio router becomes the new preference.
                // A freshly enumerating endpoint may not yet have an ownership identity.
                if (active.Contains(now)) preferred[slot] = now;
                continue;
            }
            string? target = preferred.GetValueOrDefault(slot);
            if (target is not null && !owned.Contains(target) && active.Contains(target))
                changes.Add(new(slot, now, target));
        }
        return changes;
    }
}
