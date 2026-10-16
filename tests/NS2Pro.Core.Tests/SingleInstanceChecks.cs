using System.Diagnostics;
using System.Runtime.Versioning;
using NS2Pro.App.Services;

[SupportedOSPlatform("windows")]
internal static class SingleInstanceChecks
{
    internal static int? RunProbe(string[] args)
    {
        if (args.Length != 3 || args[0] != "--instance-probe") return null;
        using var instance = new SingleInstance(args[2]);
        if (args[1] == "secondary") {
            if (instance.IsPrimary) return 1;
            instance.RequestActivation(); return 0;
        }
        if (!instance.IsPrimary) return 2;
        if (args[1] == "abandon") {
            Console.WriteLine("READY"); Console.Out.Flush(); Console.ReadLine();
            Environment.Exit(0); // Simulate process termination without the instance's finally/Dispose.
        }
        return 0;
    }

    internal static int Run()
    {
        int passed = 0;
        void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; }
        string key = "Local\\NS2Pro.Test." + Guid.NewGuid().ToString("N");
        int activations = 0;
        using (var first = new SingleInstance(key)) {
            Check(first.IsPrimary, "First process owns the instance");
            using (var early = StartProbe("secondary", key)) Finish(early);
            first.Listen(() => Interlocked.Increment(ref activations));
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref activations) > 0, 3000), "Duplicate during cold startup activates the window once it is ready");
            int before = Volatile.Read(ref activations);
            var duplicates = Enumerable.Range(0, 8).Select(_ => StartProbe("secondary", key)).ToArray();
            try {
                foreach (var duplicate in duplicates) Finish(duplicate);
                Check(duplicates.All(process => process.ExitCode == 0), "Eight concurrent duplicate processes exit without taking ownership");
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref activations) > before, 3000), "Running and tray-hidden instance receives activation requests");
            } finally { foreach (var duplicate in duplicates) duplicate.Dispose(); }
        }
        using (var next = StartProbe("owner", key)) { Finish(next); Check(next.ExitCode == 0, "Normal exit releases both instance locks for a later launch"); }

        using (var oldPreview = new Mutex(true, key + ".Preview")) {
            try { using var blocked = StartProbe("secondary", key); Finish(blocked); Check(blocked.ExitCode == 0, "Legacy preview also blocks a second instance"); }
            finally { oldPreview.ReleaseMutex(); }
        }
        using (var next = StartProbe("owner", key)) { Finish(next); Check(next.ExitCode == 0, "Failed preview-lock acquisition does not leak the main lock"); }

        using (var crashed = StartProbe("abandon", key)) {
            var ready = Task.Run(() => crashed.StandardOutput.ReadLine());
            if (!ready.Wait(10000) || ready.Result != "READY") { crashed.Kill(true); throw new Exception("Crash probe did not become ready"); }
            using var keepMutexAlive = new Mutex(false, key);
            crashed.StandardInput.WriteLine("exit"); crashed.StandardInput.Flush(); Finish(crashed);
            using var recovered = StartProbe("owner", key); Finish(recovered);
            Check(recovered.ExitCode == 0, "Abandoned mutex after a crash does not prevent a new primary process");
        }
        return passed;
    }

    private static Process StartProbe(string mode, string key)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(SingleInstanceChecks).Assembly.Location);
        foreach (var argument in new[] { "--instance-probe", mode, key }) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Unable to start instance probe");
    }

    private static void Finish(Process process)
    {
        if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Instance probe timed out"); }
        if (process.ExitCode != 0) throw new Exception("Instance probe failed: " + process.ExitCode + " " + process.StandardError.ReadToEnd());
    }
}
