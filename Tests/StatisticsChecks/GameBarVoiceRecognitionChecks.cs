using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class GameBarVoiceRecognitionChecks
{
    private static void Check(bool value, string note)
    {
        if (!value)
        {
            throw new Exception(note);
        }
    }

    public static async Task RunAsync()
    {
        await using var fixture = new BridgeFixture();
        await CheckStartupAsync(fixture);
        CheckRequestValidation(fixture);
        var first = await CheckRecordingAsync(fixture);
        await CheckStopAndReplayAsync(fixture, first);
        await CheckLeaseExpiryAsync(fixture);
        await CheckMatchChangeAsync(fixture);
        await CheckBackendAndRetryAsync(fixture);
        Console.WriteLine(
            "PASS: Game Bar local recognition bridge, readiness/results, explicit stop, acknowledgement cleanup, lease expiry, stale/mutated requests, replay protection, match/backend cancellation. No microphone or chat used."
        );
    }

    private static async Task CheckStartupAsync(BridgeFixture fixture)
    {
        var stale = fixture.Request();
        await fixture.WriteAsync(stale);
        await File.WriteAllTextAsync(fixture.StatusPath, "leftover draft");
        await fixture.PollAsync();
        Check(
            !File.Exists(fixture.RequestPath) && !File.Exists(fixture.StatusPath) && fixture.Sessions.Count == 0,
            "Startup deletes prior transport and does not replay recording"
        );
        await fixture.PollAsync();
        Check(fixture.Sessions.Count == 0, "Polling without a start never opens a microphone");
    }

    private static void CheckRequestValidation(BridgeFixture fixture)
    {
        var invalid = fixture.Request();
        invalid["session"] = "old-game";
        Check(
            OverlayVoiceRequest.Parse(invalid, fixture.Session, fixture.Now) == null,
            "Old game cannot request audio"
        );
        invalid = fixture.Request();
        invalid["updatedAt"] = fixture.Now - 4000;
        invalid["createdAt"] = fixture.Now - 4000;
        Check(
            OverlayVoiceRequest.Parse(invalid, fixture.Session, fixture.Now) == null,
            "Old lease cannot request audio"
        );
        invalid = fixture.Request();
        invalid["updatedAt"] = fixture.Now + 1;
        Check(
            OverlayVoiceRequest.Parse(invalid, fixture.Session, fixture.Now) == null,
            "Future recording requests rejected"
        );
        invalid = fixture.Request();
        invalid["text"] = "cannot send";
        Check(
            OverlayVoiceRequest.Parse(invalid, fixture.Session, fixture.Now) == null,
            "Recognition protocol cannot carry chat text"
        );
        invalid = fixture.Request();
        invalid["action"] = "send";
        Check(
            OverlayVoiceRequest.Parse(invalid, fixture.Session, fixture.Now) == null,
            "Recognition protocol cannot authorize sending"
        );
        invalid = fixture.Request();
        invalid["protocol"] = "1";
        Check(OverlayVoiceRequest.Parse(invalid, fixture.Session, fixture.Now) == null, "Malformed types rejected");
    }

    private static async Task<JObject> CheckRecordingAsync(BridgeFixture fixture)
    {
        var first = fixture.Request();
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(
            fixture.Sessions.Count == 1 && (string?)(await fixture.StatusAsync())["state"] == "preparing",
            "A fresh explicit click starts exactly one local recognizer"
        );
        var recognizer = fixture.Sessions[0];
        recognizer.Emit(new("", false, Stage: "preparing"));
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(
            (string?)(await fixture.StatusAsync())["state"] == "preparing"
                && (string?)(await fixture.StatusAsync())["hint"] == "",
            "A silent preparation update cannot make the microphone ready"
        );
        recognizer.Emit(new("", false, Message: "可以说话了 · 测试麦克风"));
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(
            (string?)(await fixture.StatusAsync())["state"] == "recording",
            "Microphone readiness is reported rather than assumed"
        );
        recognizer.Emit(new("临时假设", false));
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(
            (string?)(await fixture.StatusAsync())["text"] == ""
                && (string?)(await fixture.StatusAsync())["hint"] == "",
            "Hypotheses cannot become drafts or redundant process messages"
        );
        recognizer.Emit(new("等队友到齐", true));
        recognizer.Emit(new("再开团。", true));
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        var recognized = (await fixture.StatusAsync());
        Check(
            (string?)recognized["text"] == "等队友到齐 再开团。" && fixture.Sessions.Count == 1,
            "Final chunks reach the widget and keep-alives cannot reopen the microphone"
        );
        first["ack"] = recognized["sequence"];
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(!File.Exists(fixture.StatusPath), "Acknowledged unconfirmed draft is removed from transport");
        return first;
    }

    private static async Task CheckStopAndReplayAsync(BridgeFixture fixture, JObject first)
    {
        var recognizer = fixture.Sessions[0];
        first["action"] = "stop";
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        await recognizer.Finished.Task;
        await fixture.PollAsync();
        Check(
            recognizer.Stopped
                && recognizer.Disposed
                && (bool?)(await fixture.StatusAsync())["complete"] == true
                && (string?)(await fixture.StatusAsync())["text"] == "等队友到齐 再开团。",
            "Stopping flushes final results and releases the microphone"
        );
        first["ack"] = (await fixture.StatusAsync())["sequence"];
        fixture.Now += 100;
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(!File.Exists(fixture.StatusPath), "Terminal draft is removed after acknowledgement");
        File.Delete(fixture.RequestPath);
        fixture.Now += 4000;
        await fixture.PollAsync();
        recognizer.Emit(new("迟到内容", true));
        await fixture.WriteAsync(first);
        await fixture.PollAsync();
        Check(
            fixture.Sessions.Count == 1 && !File.Exists(fixture.StatusPath),
            "Consumed id cannot replay recording or late results"
        );
    }

    private static async Task CheckLeaseExpiryAsync(BridgeFixture fixture)
    {
        var second = fixture.Request();
        await fixture.WriteAsync(second);
        await fixture.PollAsync();
        Check(fixture.Sessions.Count == 2, "A new user click can start a new session");
        fixture.Sessions[1].Emit(new("尚未确认", true));
        fixture.Now += 100;
        await fixture.WriteAsync(second);
        await fixture.PollAsync();
        Check(File.Exists(fixture.StatusPath), "Draft can be read while recording");
        await File.WriteAllTextAsync(fixture.RequestPath, "{incomplete request");
        fixture.Now += 3100;
        await fixture.PollAsync();
        await fixture.Sessions[1].Finished.Task;
        Check(
            fixture.Sessions[1].Cancelled && !File.Exists(fixture.StatusPath),
            "Unreadable widget heartbeat cancels audio and removes draft transport"
        );
    }

    private static async Task CheckMatchChangeAsync(BridgeFixture fixture)
    {
        var third = fixture.Request();
        await fixture.WriteAsync(third);
        await fixture.PollAsync();
        Check(fixture.Sessions.Count == 3, "Next click works after microphone cancellation");
        fixture.Session = "next-game";
        await fixture.PollAsync();
        await fixture.Sessions[2].Finished.Task;
        Check(
            fixture.Sessions[2].Cancelled && !File.Exists(fixture.RequestPath) && !File.Exists(fixture.StatusPath),
            "Match change cancels audio and invalidates old drafts"
        );
    }

    private static async Task CheckBackendAndRetryAsync(BridgeFixture fixture)
    {
        var fourth = fixture.Request();
        await fixture.WriteAsync(fourth);
        await fixture.PollAsync(false);
        Check(fixture.Sessions.Count == 3, "Disabled backend cannot start audio");
        await fixture.PollAsync();
        await fixture.WriteAsync(fixture.Request(action: "cancel"));
        await fixture.PollAsync();
        Check(fixture.Sessions.Count == 3, "Cancellation without a start cannot open audio");
        await fixture.WriteAsync(fixture.Request());
        await fixture.PollAsync();
        Check(fixture.Sessions.Count == 4, "Recording can resume after backend re-enabled");
        fixture.Sessions[3].Fail();
        await fixture.Sessions[3].Finished.Task;
        for (int i = 0; i < 20 && !(bool?)(await fixture.StatusAsync())["complete"] == true; i++)
        {
            await Task.Delay(10);
            await fixture.PollAsync();
        }
        await fixture.PollAsync();
        Check(
            (string?)(await fixture.StatusAsync())["state"] == "error"
                && (bool?)(await fixture.StatusAsync())["complete"] == true
                && ((string?)(await fixture.StatusAsync())["hint"] ?? "").Contains("麦克风"),
            "Recognition failures are reported and unlock the editor"
        );
        await fixture.WriteAsync(fixture.Request());
        await fixture.PollAsync();
        Check(fixture.Sessions.Count == 5, "A failed recognizer can be retried by a new explicit click");
        fixture.Service.Dispose();
        await fixture.Sessions[4].Finished.Task;
        Check(
            fixture.Sessions[4].Cancelled
                && !File.Exists(fixture.RequestPath)
                && !File.Exists(fixture.StatusPath)
                && !File.Exists(Path.Combine(fixture.Directory, "chat-request.json")),
            "Shutdown cleans transport; recording never writes a chat request"
        );
    }

    private sealed class BridgeFixture : IAsyncDisposable
    {
        public string Directory { get; } =
            Path.Combine(Path.GetTempPath(), "league-voice-bridge-" + Guid.NewGuid().ToString("N"));
        public long Now { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public string Session { get; set; } = "main-instance:game/3140";
        public List<FakeSession> Sessions { get; } = new();
        public GameBarVoiceRecognition Service { get; }
        public string RequestPath => Path.Combine(Directory, "voice-request.json");
        public string StatusPath => Path.Combine(Directory, "voice-status.json");

        public BridgeFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Service = new GameBarVoiceRecognition(() =>
            {
                var session = new FakeSession();
                Sessions.Add(session);
                return session;
            });
        }

        public Task PollAsync(bool enabled = true) => Service.PollAsync(Directory, Session, enabled, time: Now);

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

        public async Task<JObject> StatusAsync() => JObject.Parse(await File.ReadAllTextAsync(StatusPath));

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            // The bridge must finish cancellation before its transport directory is removed.
            await Task.WhenAll(Sessions.Select(session => session.Finished.Task)).WaitAsync(TimeSpan.FromSeconds(5));
            System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class FakeSession : IDesktopVoiceSession
    {
        private readonly TaskCompletionSource<VoiceRecognitionOutcome> _result = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;
        public event Action<VoiceRecognitionUpdate>? Update;
        public bool Stopped,
            Cancelled,
            Disposed;

        public void Emit(VoiceRecognitionUpdate update) => Update?.Invoke(update);

        public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
        {
            _registration = token.Register(() =>
            {
                Cancelled = true;
                _result.TrySetResult(new(true));
            });
            return _result.Task;
        }

        public void Stop()
        {
            Stopped = true;
            _result.TrySetResult(new(false));
        }

        public void Fail() => _result.TrySetException(new UnauthorizedAccessException());

        public void Dispose()
        {
            _registration.Dispose();
            Disposed = true;
            if (!Stopped)
            {
                Cancelled = true;
                _result.TrySetResult(new(true));
            }
            Finished.TrySetResult();
        }
    }
}
