using NS2Pro.Core;

namespace NS2Pro.App.Services;

public interface IGameOutput : IDisposable
{
    bool FeedbackAvailable { get; }
    event Action<byte, byte>? RumbleChanged;
    void Update(XboxReport report);
}
