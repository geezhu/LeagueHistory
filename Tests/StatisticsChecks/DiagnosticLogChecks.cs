using League.Services;

internal static class DiagnosticLogChecks
{
    public static async Task RunAsync()
    {
        await BoundedQueueAsync();
        await DailyPathAsync();
        WriteNowFlushesQueue();
        Retention();
        Console.WriteLine(
            "PASS: shared log writer queue bound, rotation, daily files, crash flush and 7-day / 20 MB retention."
        );
    }

    private static async Task BoundedQueueAsync()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "bounded.log");
        var log = new LogFileWriter(path, 1024 * 1024, 3);
        // Hold the file so the first write fails and later lines stay queued behind the bound.
        await using (new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            for (int i = 0; i < 10; i++)
            {
                log.Write("line " + i);
            }

            await log.FlushAsync();
            Check(log.LastError?.Contains("IOException") == true, "A locked log reports its write failure");
        }

        log.Write("after\nunlock");
        await log.FlushAsync();
        var lines = await File.ReadAllLinesAsync(path);
        Check(lines.Length > 0 && lines[^1] == "after unlock", "Records stay single line");
        Check(lines.Length <= 4, "The pending queue keeps only the newest records");
        Check(log.LastError == null, "A successful write clears the failure");
    }

    private static async Task DailyPathAsync()
    {
        using var directory = new TemporaryDirectory();
        string day = "2026-10-15";
        var log = new LogFileWriter(() => Path.Combine(directory.Path, $"app-{day}.log"), 1024, 100);
        log.Write("first day");
        await log.FlushAsync();
        day = "2026-10-16";
        log.Write("next day");
        await log.FlushAsync();
        string first = await File.ReadAllTextAsync(Path.Combine(directory.Path, "app-2026-10-15.log"));
        string next = await File.ReadAllTextAsync(Path.Combine(directory.Path, "app-2026-10-16.log"));
        Check(
            first.Contains("first day") && next.Contains("next day"),
            "A path provider moves records to the new day's file"
        );

        string rotating = Path.Combine(directory.Path, "rotating.log");
        await File.WriteAllTextAsync(rotating, new string('x', 2048));
        var bounded = new LogFileWriter(rotating, 1024, 100);
        bounded.Write("new");
        await bounded.FlushAsync();
        Check(File.Exists(rotating + ".previous") && new FileInfo(rotating).Length < 100, "Oversized logs rotate");
    }

    private static void WriteNowFlushesQueue()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "crash.log");
        var log = new LogFileWriter(path, 1024 * 1024, 100);
        log.WriteNow("crash");
        Check(File.ReadAllLines(path).SequenceEqual(new[] { "crash" }), "WriteNow writes synchronously");
    }

    private static void Retention()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        string Write(string name, int bytes, int daysOld)
        {
            string path = Path.Combine(directory.Path, name);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTimeUtc(path, now.AddDays(-daysOld));
            return path;
        }

        string expired = Write("tier-chat-1234.log", 10, 8);
        string expiredPrevious = Write("room-players.log.previous", 10, 9);
        string oldest = Write("app-2026-10-10.log", 600, 6);
        string middle = Write("app-2026-10-12.log", 600, 4);
        string newest = Write("app-2026-10-16.log", 600, 0);
        string other = Write("notes.json", 10, 30);
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));

        int deleted = LogRetention.Clean(directory.Path, now, maxTotalBytes: 1300);
        Check(!File.Exists(expired) && !File.Exists(expiredPrevious), "Logs older than seven days are removed");
        Check(!File.Exists(oldest) && File.Exists(middle) && File.Exists(newest), "Over the size cap, oldest go first");
        Check(File.Exists(other), "Only log files are cleaned");
        Check(deleted == 3, "Retention reports how many files it removed");
        Check(LogRetention.Clean(Path.Combine(directory.Path, "missing"), now) == 0, "A missing directory is fine");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "league-log-checks-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A background writer may still hold a file for a moment; the temp folder is cleaned by Windows.
            }
        }
    }
}
