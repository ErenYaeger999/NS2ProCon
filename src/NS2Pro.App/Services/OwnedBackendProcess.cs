using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NS2Pro.App.Services;

/// <summary>Only the process launched by this owner is stopped, including on parent termination.</summary>
internal sealed class OwnedBackendProcess : IDisposable
{
    private readonly SafeFileHandle job;
    public Process Process { get; }
    public OwnedBackendProcess(ProcessStartInfo start)
    {
        job = new SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
        if (job.IsInvalid) throw new IOException("无法建立增强组件的生命周期管理");
        var info = new ExtendedLimitInformation(); info.BasicLimitInformation.LimitFlags = 0x2000;
        if (!SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimitInformation>())) {
            job.Dispose(); throw new IOException("无法设置增强组件退出清理");
        }
        try { Process = Process.Start(start) ?? throw new IOException("无法启动增强组件"); }
        catch { job.Dispose(); throw; }
        if (!AssignProcessToJobObject(job, Process.Handle)) {
            try { Process.Kill(true); } catch (Exception) { }
            Process.Dispose(); job.Dispose(); throw new IOException("无法托管增强组件进程");
        }
        // Drain without recording backend credentials or user paths. Explicit key-file avoids secret output.
        _ = DrainAsync(Process.StandardOutput); _ = DrainAsync(Process.StandardError);
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        try { char[] buffer = new char[2048]; while (await reader.ReadAsync(buffer) > 0) { } }
        catch (Exception) { }
    }
    public void Dispose()
    {
        job.Dispose(); // KILL_ON_JOB_CLOSE affects only this job's own descendants.
        try { if (!Process.HasExited) Process.WaitForExit(1500); } catch (Exception) { }
        Process.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation {
        public long ProcessUserTimeLimit, JobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation {
        public BasicLimitInformation BasicLimitInformation; public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimitInformation info, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
