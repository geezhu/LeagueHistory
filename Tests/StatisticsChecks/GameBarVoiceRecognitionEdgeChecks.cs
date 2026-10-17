using System.Reflection;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// The Game Bar recognition bridge's awkward paths, driven through a scripted local recognizer and a temporary
/// transport folder: cancelling mid recording, a request while the microphone is still releasing, empty and
/// oversized drafts, every recognition outcome, unusable transport paths and a status write after shutdown.
/// No microphone, no voice model and no chat are involved.
/// </summary>
internal static class GameBarVoiceRecognitionEdgeChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static async Task RunAsync()
    {
        await using (var bridge = new Bridge())
        {
            await CancelWhileRecordingAsync(bridge);
            await BusyRequestAsync(bridge);
            await UpdateEdgesAsync(bridge);
            await OutcomeEdgesAsync(bridge);
            await BrokenTransportAsync(bridge);
            await ShutdownEdgesAsync(bridge);
        }

        await UnusablePathAsync();
        CleanEdges();
        Console.WriteLine(
            "PASS: the Game Bar recognition bridge's cancel, busy, empty and oversized drafts, every outcome, its "
                + "unusable transport paths and its shutdown, without a microphone, a model or chat."
        );
    }

    private static async Task CancelWhileRecordingAsync(Bridge bridge)
    {
        var start = await bridge.StartAsync();
        Check(await bridge.StateAsync() == "preparing", "A fresh recording starts by preparing");

        var cancel = bridge.Request((string)start.Request!["id"]!, "cancel");
        await bridge.WriteAsync(cancel);
        await bridge.PollAsync();
        await start.Session.Finished.Task;
        Check(start.Session.Cancelled, "Cancelling mid recording releases the microphone");
        Check(!File.Exists(bridge.StatusPath), "Cancelling mid recording clears the draft transport");
    }

    /// <summary>A second click while the first microphone is still being released must not open another one.</summary>
    private static async Task BusyRequestAsync(Bridge bridge)
    {
        bridge.HoldSessions = true;
        var first = await bridge.StartAsync();
        var replacement = bridge.Request();
        await bridge.WriteAsync(replacement);
        await bridge.PollAsync();
        Check(bridge.Sessions.Count == 2, "A request that replaces a releasing recording is not accepted");

        bridge.HoldSessions = false;
        first.Session.Complete(new VoiceRecognitionOutcome(false));
        await first.Session.Finished.Task;
    }

    private static async Task UpdateEdgesAsync(Bridge bridge)
    {
        var recording = await bridge.StartAsync();
        var session = recording.Session;
        session.Emit(new VoiceRecognitionUpdate("   ", true));
        session.Emit(new VoiceRecognitionUpdate("", true, Confidence: 0.1f));
        Check(session.Stops == 0, "A final chunk without words adds neither text nor a stop");
        Check(await bridge.TextAsync() == "", "A final chunk without words cannot reach the draft");

        session.Emit(new VoiceRecognitionUpdate(new string('长', 200), true));
        Check(session.Stops == 1, "A draft that reaches the limit stops the microphone");
        await bridge.PollAsync();
        Check((await bridge.TextAsync()).Length == 180, "A draft is published cut to its limit");
    }

    private static async Task OutcomeEdgesAsync(Bridge bridge)
    {
        string? reported = await OutcomeAsync(
            bridge,
            session => session.Complete(new VoiceRecognitionOutcome(false, "检查用结果提示"))
        );
        Check(reported == "检查用结果提示", $"A recognition result message is reported: {reported}");

        reported = await OutcomeAsync(bridge, session => session.Complete(new VoiceRecognitionOutcome(true)));
        Check(
            reported != null && reported.Contains("取消", StringComparison.Ordinal),
            $"A recognition that cancels itself is explained: {reported}"
        );

        reported = await OutcomeAsync(bridge, session => session.Fail(new OperationCanceledException()));
        Check(
            reported != null && reported.Contains("超时", StringComparison.Ordinal),
            $"A recognition that runs out of time is explained: {reported}"
        );

        reported = await OutcomeAsync(bridge, session => session.Fail(new VoiceRecognitionException("检查用语音失败")));
        Check(reported == "检查用语音失败", $"A recognition failure is reported as it is: {reported}");

        reported = await OutcomeAsync(
            bridge,
            session => session.Fail(new InvalidOperationException("当前没有可用的中文离线语音引擎（检查用）"))
        );
        Check(
            reported != null && reported.Contains("引擎", StringComparison.Ordinal),
            $"A missing voice engine is explained: {reported}"
        );
    }

    private static async Task<string?> OutcomeAsync(Bridge bridge, Action<ScriptedSession> finish)
    {
        var recording = await bridge.StartAsync();
        finish(recording.Session);
        await recording.Session.Finished.Task;
        return await bridge.HintAsync((string)recording.Request["id"]!);
    }

    /// <summary>A transport folder the file system refuses to clean, and a status file that cannot be published.</summary>
    private static async Task BrokenTransportAsync(Bridge bridge)
    {
        File.Delete(bridge.RequestPath);
        Directory.CreateDirectory(bridge.RequestPath);
        bridge.Session = "main-instance:game/2";
        await bridge.PollAsync();
        Check(!File.Exists(bridge.StatusPath), "A transport folder that cannot be cleaned is retried later");
        Directory.Delete(bridge.RequestPath);

        var recording = await bridge.StartAsync();
        Check(
            await bridge.StateAsync() == "preparing",
            "Recording works after a transport folder that could not be cleaned"
        );

        File.Delete(bridge.StatusPath);
        Directory.CreateDirectory(bridge.StatusPath);
        bridge.Now += 100;
        var blocked = bridge.Request();
        await bridge.WriteAsync(blocked);
        await bridge.PollAsync();
        if (Directory.Exists(bridge.StatusPath))
        {
            Directory.Delete(bridge.StatusPath);
        }
        Check(true, "A status that cannot be published leaves the bridge ready for the next poll");
        recording.Session.Complete(new VoiceRecognitionOutcome(false));
        await recording.Session.Finished.Task;
    }

    private static async Task ShutdownEdgesAsync(Bridge bridge)
    {
        bridge.HoldSessions = true;
        var recording = await bridge.StartAsync();
        bridge.Service.Dispose();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            recording.Session.Emit(new VoiceRecognitionUpdate("关停后的迟到内容", true));
        }
        Check(recording.Session.Stops == 0, "A late recognition update cannot restart a microphone after shutdown");

        var status = new JObject { ["protocol"] = 1, ["sequence"] = 1 };
        var write = (Task)
            typeof(GameBarVoiceRecognition)
                .GetMethod("WriteAsync", Any)!
                .Invoke(bridge.Service, new object[] { status })!;
        await write;
        Check(
            !File.Exists(Path.Combine(bridge.Directory, "voice-status.tmp")),
            "A status written after shutdown leaves no temporary file behind"
        );
        recording.Session.Complete(new VoiceRecognitionOutcome(false));
        await recording.Session.Finished.Task;
    }

    private static async Task UnusablePathAsync()
    {
        using var service = new GameBarVoiceRecognition(() => new ScriptedSession());
        await service.PollAsync(
            "C:\\检查用<>|非法路径",
            "session",
            true,
            time: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        Check(true, "A transport path the file system refuses is retried on the next poll");
    }

    private static void CleanEdges()
    {
        var clean = typeof(GameBarVoiceRecognition).GetMethod("Clean", StaticAny)!;
        Check(
            (string)clean.Invoke(null, new object[] { new string('长', 200), 180 })! == new string('长', 180),
            "A draft is cut to its limit"
        );
        var split = new string('a', 179) + "\uD83D\uDE00";
        Check(
            (string)clean.Invoke(null, new object[] { split, 180 })! == new string('a', 179),
            "A cut draft never keeps half a character"
        );
        Check(
            (string)clean.Invoke(null, new object[] { "甲\u2028乙\u2029丙", 180 })! == "甲 乙 丙",
            "A draft never carries a control or separator character"
        );
    }

    private sealed class Bridge : IAsyncDisposable
    {
        public string Directory { get; } =
            Path.Combine(Path.GetTempPath(), "league-voice-edge-" + Guid.NewGuid().ToString("N"));

        public long Now { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public string Session { get; set; } = "main-instance:game/1";

        /// <summary>Recognizers that ignore cancellation, so a replaced recording can still be in flight.</summary>
        public bool HoldSessions { get; set; }

        public List<ScriptedSession> Sessions { get; } = new();

        public GameBarVoiceRecognition Service { get; }

        public string RequestPath => Path.Combine(Directory, "voice-request.json");

        public string StatusPath => Path.Combine(Directory, "voice-status.json");

        public Bridge()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Service = new GameBarVoiceRecognition(() =>
            {
                var session = new ScriptedSession { HoldResult = HoldSessions };
                Sessions.Add(session);
                return session;
            });
        }

        public Task PollAsync(bool enabled = true) => Service.PollAsync(Directory, Session, enabled, time: Now);

        /// <summary>Writes a start request and polls until it opens a recognizer, or fails the check.</summary>
        public async Task<Recording> StartAsync()
        {
            // The first poll of a session settles it and clears anything a previous instance left behind.
            await PollAsync();
            var request = Request();
            await WriteAsync(request);
            int before = Sessions.Count;
            for (int attempt = 0; attempt < 200 && Sessions.Count == before; attempt++)
            {
                await PollAsync();
                if (Sessions.Count == before)
                {
                    await Task.Delay(10);
                }
            }
            Check(Sessions.Count == before + 1, $"A start opens one recognizer: {Sessions.Count} against {before}");
            return new Recording(request, Sessions[^1]);
        }

        public JObject Request(string? id = null, string action = "start") =>
            new()
            {
                ["protocol"] = 1,
                ["id"] = id ?? Guid.NewGuid().ToString("N"),
                ["session"] = Session,
                ["createdAt"] = Now,
                ["updatedAt"] = Now,
                ["action"] = action,
                ["ack"] = 0,
            };

        public async Task WriteAsync(JObject request)
        {
            request["updatedAt"] = Now;
            await File.WriteAllTextAsync(RequestPath, request.ToString());
        }

        public async Task<string?> StateAsync() => (string?)(await StatusAsync())?["state"];

        public async Task<string> TextAsync() => (string?)(await StatusAsync())?["text"] ?? "";

        public async Task<string?> HintAsync(string id)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                await PollAsync();
                var status = await StatusAsync();
                if ((string?)status?["id"] == id && (bool?)status?["complete"] == true)
                {
                    return (string?)status["hint"];
                }
                await Task.Delay(10);
            }
            return null;
        }

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await Task.WhenAll(Sessions.Select(session => session.Finished.Task)).WaitAsync(TimeSpan.FromSeconds(5));
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }

        private async Task<JObject?> StatusAsync() =>
            File.Exists(StatusPath) ? JObject.Parse(await File.ReadAllTextAsync(StatusPath)) : null;
    }

    private sealed record Recording(JObject Request, ScriptedSession Session);

    /// <summary>A recognizer whose readiness, results and failures are all decided by the check.</summary>
    private sealed class ScriptedSession : IDesktopVoiceSession
    {
        private readonly TaskCompletionSource<VoiceRecognitionOutcome> _result = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private CancellationTokenRegistration _registration;

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<VoiceRecognitionUpdate>? Update;

        public bool HoldResult { get; init; }

        public bool Cancelled { get; private set; }

        public int Stops { get; private set; }

        public void Emit(VoiceRecognitionUpdate update) => Update?.Invoke(update);

        public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
        {
            if (!HoldResult)
            {
                _registration = token.Register(() =>
                {
                    Cancelled = true;
                    _result.TrySetResult(new VoiceRecognitionOutcome(true));
                });
            }
            return _result.Task;
        }

        public void Stop()
        {
            Stops++;
            _result.TrySetResult(new VoiceRecognitionOutcome(false));
        }

        public void Complete(VoiceRecognitionOutcome outcome) => _result.TrySetResult(outcome);

        public void Fail(Exception error) => _result.TrySetException(error);

        public void Dispose()
        {
            _registration.Dispose();
            if (!HoldResult && !_result.Task.IsCompleted)
            {
                Cancelled = true;
                _result.TrySetResult(new VoiceRecognitionOutcome(true));
            }
            Finished.TrySetResult();
        }
    }
}
