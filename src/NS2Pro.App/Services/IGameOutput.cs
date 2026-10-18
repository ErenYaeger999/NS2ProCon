using NS2Pro.Core;

namespace NS2Pro.App.Services;

public interface IGameOutput : IDisposable
{
    bool FeedbackAvailable { get; }
    event Action<byte, byte>? RumbleChanged;
    event Action<long>? InputForwarded;
    void Update(XboxReport report, long receivedAt = 0);
}
