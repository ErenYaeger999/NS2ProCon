namespace NS2Pro.App.Services;

public enum ConnectionPhase { Idle, Preparing, Searching, Connecting, Connected, Stopping, Error, WaitingForInput }

public sealed record ConnectionInfo(ConnectionPhase Phase, string Message, string? Address = null, double Hertz = 0, long Reports = 0)
{
    internal bool HasLink => Phase is ConnectionPhase.Connected or ConnectionPhase.WaitingForInput;
    internal bool CanStop => HasLink || Phase is ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting;
    internal bool IsActive => CanStop || Phase == ConnectionPhase.Stopping;
    internal bool CanStart => Phase is ConnectionPhase.Idle or ConnectionPhase.Error;
    internal string ActionText => HasLink ? "断开 PC 连接" : Phase switch {
        ConnectionPhase.Preparing or ConnectionPhase.Searching or ConnectionPhase.Connecting => "取消连接",
        ConnectionPhase.Stopping => "正在断开…",
        ConnectionPhase.Error => "重试连接",
        _ => "连接手柄"
    };
}
