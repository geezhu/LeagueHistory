using League.Services;

internal static class UiHangMonitorChecks
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-hang-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "app.log");
        long now = 0;
        var queued = new Queue<Action>();
        try
        {
            var log = new AppLog(() => path, new DiagnosticRedactor("salt", ""), () => now);
            var monitor = new UiHangMonitor(queued.Enqueue, () => "游戏阶段 ChampSelect", log, () => now);

            // A responsive UI thread runs each probe before the next tick: nothing is logged.
            for (int i = 0; i < 20; i++)
            {
                monitor.Tick();
                Check(queued.Count == 1, "One probe is in flight at a time");
                queued.Dequeue()();
                now += 250;
            }

            // The UI thread stops for 3.5 seconds: one start record, no extra probes queued behind it.
            monitor.Tick();
            for (int i = 0; i < 14; i++)
            {
                now += 250;
                monitor.Tick();
            }
            Check(queued.Count == 1, "A blocked UI thread does not pile up probes");
            queued.Dequeue()();

            // A throwing description must not stop the record.
            var failing = new UiHangMonitor(
                queued.Enqueue,
                () => throw new InvalidOperationException(),
                log,
                () => now
            );
            failing.Tick();
            now += 2500;
            failing.Tick();

            // A window that is already gone stops the monitor instead of throwing from the timer.
            var closed = new UiHangMonitor(_ => throw new ObjectDisposedException("form"), () => "", log, () => now);
            closed.Tick();

            await log.FlushAsync();
            var lines = (await File.ReadAllLinesAsync(path)).Skip(1).ToArray();
            Check(lines.Length == 3, "Start and recovery are logged once each: " + string.Join(" / ", lines));
            Check(lines[0].Contains("界面已 2 秒无响应（游戏阶段 ChampSelect）"), "The hang start names the phase");
            Check(lines[1].Contains("界面恢复响应，共卡住 3.5 秒"), "Recovery reports the full duration");
            Check(lines[2].Contains("状态未知：InvalidOperationException"), "A failing description is replaced");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
        Console.WriteLine("PASS: UI hang start, recovery duration, single in-flight probe and closed-window handling.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
