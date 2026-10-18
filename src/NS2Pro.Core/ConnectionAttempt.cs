namespace NS2Pro.Core;

/// <summary>Keep the failed stage in the user message and preserve native details in logs.</summary>
public sealed class ConnectionAttempt
{
    public static bool CanUseAdvertisement(ulong address, short rssi, bool anonymous) => address != 0 && rssi != -127 && !anonymous;
    public string Stage { get; set; } = "打开蓝牙设备";
    public string Describe(Exception error)
    {
        string reason = error is TimeoutException or OperationCanceledException ? "Windows 蓝牙操作超时，请重新长按 SYNC 后重试。" : error.Message;
        if (error is UnauthorizedAccessException || error.HResult == unchecked((int)0x80070005))
            reason = "Windows 拒绝访问手柄。请检查系统蓝牙权限，并退出其他手柄连接工具后重试。";
        return $"连接未完成 · {Stage}：{reason}";
    }
}
