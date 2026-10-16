using System.Runtime.InteropServices;

namespace NS2Pro.Core;

public static class SystemRequirements
{
    public const int MinimumWindowsBuild = 19041;
    public static string? GetIssue(Version version, Architecture architecture)
    {
        if (architecture != Architecture.X64) return "当前版本适用于 Intel / AMD 的 64 位 Windows；暂不支持 ARM64 和 32 位系统。";
        if (version.Major < 10 || version.Build < MinimumWindowsBuild)
            return "需要 Windows 10 2004（19041）或更新版本，建议使用 Windows 11。";
        return null;
    }
}
