using League.Services;

internal static class AppLogChecks
{
    public static async Task RunAsync()
    {
        Redaction();
        await SessionAndStatesAsync();
        await RepeatedWarningsAsync();
        CrashMarker();
        Console.WriteLine(
            "PASS: app log header, state de-duplication, crash flush, crash marker and redaction of tokens, paths and player IDs."
        );
    }

    private static void Redaction()
    {
        var redactor = new DiagnosticRedactor("salt", @"C:\Users\张三");
        string puuid = new string('a', 39) + "-_" + new string('B', 37);
        string uuid = "0f8fad5b-d9cb-469f-a165-70867728950e";
        string raw =
            @"open C:\Users\张三\AppData\Local\League\x.log and c:/users/张三/Desktop "
            + "--remoting-auth-token=AbC123_- --app-port=53210 "
            + "https://riot:s3cr3t@127.0.0.1:53210/lol-summoner "
            + "Authorization: Bearer abcdefghijklmnop "
            + "jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U "
            + $"player {puuid} and {uuid} again {uuid.ToUpperInvariant()}";
        string text = redactor.Redact(raw);
        Check(!text.Contains("张三") && text.Contains(@"%USERPROFILE%\AppData"), "User profile paths are replaced");
        Check(!text.Contains("AbC123_-") && text.Contains("--remoting-auth-token=***"), "Client token is removed");
        Check(text.Contains("--app-port=53210"), "Non-secret arguments stay readable");
        Check(!text.Contains("s3cr3t") && text.Contains("https://riot:***@127.0.0.1"), "URL credentials are removed");
        Check(!text.Contains("abcdefghijklmnop") && text.Contains("Bearer ***"), "Authorization headers are removed");
        Check(!text.Contains("eyJhbGci"), "JWTs are removed");
        Check(!text.Contains(puuid) && !text.Contains(uuid), "Player IDs are pseudonymized");
        var ids = System.Text.RegularExpressions.Regex.Matches(text, "id:[0-9A-F]{8}").Select(m => m.Value).ToList();
        Check(ids.Count == 3 && ids[1] == ids[2] && ids[0] != ids[1], "The same ID maps to the same pseudonym");
        Check(
            new DiagnosticRedactor("other", "").Redact(uuid) != redactor.Redact(uuid),
            "Pseudonyms depend on the salt"
        );
    }

    private static async Task SessionAndStatesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-app-log-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "app.log");
        try
        {
            var log = new AppLog(() => path, new DiagnosticRedactor("salt", ""));
            log.State(LogCategory.Connection, "lcu", "客户端尚未启动");
            log.State(LogCategory.Connection, "lcu", "客户端尚未启动");
            log.State(LogCategory.Connection, "lcu", "已连接客户端接口");
            log.State(LogCategory.Sgp, "sgp", "客户端尚未启动");
            log.Warn(LogCategory.Update, "检查更新失败", new HttpRequestException("timeout", new IOException("reset")));
            await log.FlushAsync();
            var lines = await File.ReadAllLinesAsync(path);
            Check(lines.Length == 5, "One header plus each state change and the warning");
            Check(lines[0].Contains("[App] session version=") && lines[0].Contains("commit="), "Header has the build");
            Check(lines[1].Contains("[info] [Connection] 客户端尚未启动"), "First state is logged");
            Check(lines[2].Contains("已连接客户端接口"), "A changed state is logged once");
            Check(lines[3].Contains("[Sgp]"), "States are tracked per key");
            Check(
                lines[4].Contains("[warn] [Update] 检查更新失败 | HttpRequestException <- IOException"),
                "Warnings name the exception and its root cause"
            );

            var crash = new AppLog(() => path + ".crash", new DiagnosticRedactor("salt", ""));
            Exception thrown;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException ex)
            {
                // Only a thrown exception has a stack trace to record.
                thrown = ex;
            }
            crash.Crash("未处理异常", thrown);
            var crashLines = await File.ReadAllLinesAsync(path + ".crash");
            Check(
                crashLines.Length == 2 && crashLines[1].Contains("[fatal] [Crash]") && crashLines[1].Contains("at "),
                "A crash is written synchronously with its stack"
            );
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task RepeatedWarningsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-app-log-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "app.log");
        long now = 0;
        try
        {
            var log = new AppLog(() => path, new DiagnosticRedactor("salt", ""), () => now);
            var offline = new HttpRequestException("offline");
            for (int i = 0; i < 25; i++)
            {
                log.Warn(LogCategory.Ui, "OP.GG icon", offline);
                now += 1000;
            }

            log.Warn(LogCategory.Ui, "OP.GG ranking icon", offline);
            now += 60_000;
            log.Warn(LogCategory.Ui, "OP.GG icon", offline);

            log.State(LogCategory.Automation, "auto-pick", "自动预选：未开启");
            log.ResetStates("auto-pick");
            log.State(LogCategory.Automation, "auto-pick", "自动预选：未开启");
            await log.FlushAsync();

            var lines = (await File.ReadAllLinesAsync(path)).Skip(1).ToArray();
            Check(lines.Length == 5, "Repeats inside a minute collapse into one line: " + string.Join(" / ", lines));
            Check(
                lines[0].Contains("OP.GG icon | HttpRequestException") && !lines[0].Contains("又出现"),
                "First warning"
            );
            Check(lines[1].Contains("OP.GG ranking icon"), "A different message is not suppressed");
            Check(lines[2].Contains("此前 60 秒内又出现 24 次"), "The next line reports how many were suppressed");
            Check(
                lines[3].Contains("自动预选：未开启") && lines[4].Contains("自动预选：未开启"),
                "Reset states log again"
            );
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CrashMarker()
    {
        string path = Path.Combine(Path.GetTempPath(), "league-crash-" + Guid.NewGuid().ToString("N") + ".json");
        Check(CrashReporter.TakeLastCrash(path) == null, "No marker means no prompt");
        CrashReporter.WriteMarker("fatal", new InvalidOperationException("boom"), path);
        var marker = CrashReporter.TakeLastCrash(path);
        Check(marker?.Kind == "fatal" && marker.Error.Contains("InvalidOperationException"), "Marker keeps the error");
        Check(!File.Exists(path), "Reading the marker removes it so the prompt shows once");
        File.WriteAllText(path, "{broken");
        Check(CrashReporter.TakeLastCrash(path) == null, "A damaged marker is ignored");
        File.Delete(path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
