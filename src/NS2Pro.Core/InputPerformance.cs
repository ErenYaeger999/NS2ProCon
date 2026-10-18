using System.Diagnostics;

namespace NS2Pro.Core;

public sealed record InputPerformanceSnapshot(double Hertz, double IntervalMilliseconds,
    double? ForwardMedianMilliseconds, double? ForwardP95Milliseconds, int Samples);

/// <summary>Observed report cadence and host forwarding time, never end-to-end game latency.</summary>
public sealed class InputPerformance
{
    private readonly object gate = new();
    private readonly Queue<long> reports = new();
    private readonly Queue<(long At, double Milliseconds)> forwards = new();
    public void RecordReport(long at) { lock (gate) { reports.Enqueue(at); Prune(at); } }
    public void RecordForward(long receivedAt)
    {
        long now = Stopwatch.GetTimestamp();
        if (receivedAt <= 0 || receivedAt > now) return;
        lock (gate) { forwards.Enqueue((now, Stopwatch.GetElapsedTime(receivedAt, now).TotalMilliseconds)); Prune(now); }
    }
    private void Prune(long now)
    {
        while (reports.Count > 0 && (reports.Count > 512 || Stopwatch.GetElapsedTime(reports.Peek(), now).TotalSeconds > 2)) reports.Dequeue();
        while (forwards.Count > 0 && (forwards.Count > 512 || Stopwatch.GetElapsedTime(forwards.Peek().At, now).TotalSeconds > 2)) forwards.Dequeue();
    }
    public InputPerformanceSnapshot Snapshot
    {
        get { lock (gate) {
            Prune(Stopwatch.GetTimestamp());
            var times = reports.ToArray();
            double interval = times.Length < 2 ? 0 : Stopwatch.GetElapsedTime(times[0], times[^1]).TotalMilliseconds / (times.Length - 1);
            var values = forwards.Select(x => x.Milliseconds).Order().ToArray();
            return new(interval > 0 ? 1000 / interval : 0, interval,
                values.Length == 0 ? null : values[(values.Length - 1) / 2],
                values.Length == 0 ? null : values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * .95) - 1)], values.Length);
        } }
    }
    public void Reset() { lock (gate) { reports.Clear(); forwards.Clear(); } }
}
