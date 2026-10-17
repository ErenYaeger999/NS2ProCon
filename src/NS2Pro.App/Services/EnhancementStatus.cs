namespace NS2Pro.App.Services;

public enum EnhancementPhase { Unchecked, NotInstalled, Checking, Downloading, InstallingDriver, Starting, Validating, Ready, RestartRequired, Conflict, Error, Cancelled }
[Flags]
public enum MissingEnhancementComponents { None = 0, Backend = 1, Driver = 2 }
public enum EnhancementAction { Check, UseManaged, Install, Connect }

public sealed record EnhancementStatus(EnhancementPhase Phase, string Message, double Progress = 0, string Detail = "")
{
    public MissingEnhancementComponents Missing { get; init; }
    public bool Busy => Phase is EnhancementPhase.Checking or EnhancementPhase.Downloading or EnhancementPhase.InstallingDriver or EnhancementPhase.Starting or EnhancementPhase.Validating;
    public bool Ready => Phase == EnhancementPhase.Ready;

    // Detection and installation are separate actions. Errors never authorize installation.
    public EnhancementAction NextAction(bool managed) => Ready ? EnhancementAction.Connect :
        !managed ? Phase == EnhancementPhase.Unchecked ? EnhancementAction.Check : EnhancementAction.UseManaged :
        Phase == EnhancementPhase.NotInstalled && Missing != MissingEnhancementComponents.None ? EnhancementAction.Install : EnhancementAction.Check;

    public string ActionText(bool managed) => Busy ? Phase switch {
        EnhancementPhase.Checking or EnhancementPhase.Validating => "正在检测…",
        EnhancementPhase.Downloading or EnhancementPhase.InstallingDriver => "正在安装…",
        _ => "正在启动…"
    } : NextAction(managed) switch {
        EnhancementAction.Connect => "连接手柄",
        EnhancementAction.UseManaged => "改用自动管理并检测",
        EnhancementAction.Install => $"{(Missing == MissingEnhancementComponents.Backend ? "安装触觉组件" : Missing == MissingEnhancementComponents.Driver ? "安装增强驱动" : "安装增强组件")} · 约 {DownloadMegabytes:0} MB",
        _ => Phase == EnhancementPhase.Unchecked ? managed ? "检测增强环境" : "检测已有后端" : "重新检测"
    };

    public double DownloadMegabytes => Math.Round(((Missing.HasFlag(MissingEnhancementComponents.Backend) ? EnhancementComponents.Backend.Size : 0) +
        (Missing.HasFlag(MissingEnhancementComponents.Driver) ? EnhancementComponents.Driver.Size : 0)) / 1_000_000d);

    public static EnhancementStatus? PrerequisiteIssue(string? driverVersion, bool driverReady, bool filesReady, string driverDetail = "")
    {
        if (driverVersion is not null && driverVersion != EnhancementComponents.DriverVersion)
            return new(EnhancementPhase.Conflict, "已安装的 USBIP 版本不兼容，暂时无法启用增强。", Detail:
                $"检测到 {driverVersion}，当前增强后端需要 {EnhancementComponents.DriverVersion}。不会自动卸载或降级共享驱动；可使用通用模式，或在高级选项连接兼容的已有后端。");
        if (driverVersion is not null && !driverReady)
            return new(EnhancementPhase.Error, "增强驱动已安装，但当前不可用。", Detail:
                "无需反复安装组件。若安装后尚未重启，请先重启；已重启仍失败时，请根据以下错误检查或修复 USBIP 驱动，再重新检测。\n" + driverDetail);
        var missing = (driverReady ? MissingEnhancementComponents.None : MissingEnhancementComponents.Driver) |
            (filesReady ? MissingEnhancementComponents.None : MissingEnhancementComponents.Backend);
        if (missing == MissingEnhancementComponents.None) return null;
        return new(EnhancementPhase.NotInstalled, missing switch {
            MissingEnhancementComponents.Backend => "驱动已就绪，还需安装触觉组件。",
            MissingEnhancementComponents.Driver => "触觉组件已存在，还需安装增强驱动。",
            _ => "尚未安装增强组件，安装后可启用。"
        }) { Missing = missing };
    }
}
