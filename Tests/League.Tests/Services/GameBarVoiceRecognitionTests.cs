using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The Game Bar recognition bridge drives one on-demand local recognizer over a transport directory that the widget
/// polls with a lease, a sequence and an acknowledgement. Every test injects its own stub recognizer factory: the
/// bridge's default factory opens a real microphone, which tests must never do, so no test may construct the bridge
/// without one. The transport is a temporary directory and the clock is a value the test sets, so the lease and
/// sequence rules are decided by explicit numbers instead of by waiting. This class replaces the checks in
/// Tests/StatisticsChecks/GameBarVoiceRecognitionChecks.cs and GameBarVoiceRecognitionEdgeChecks.cs.
/// </summary>
public class GameBarVoiceRecognitionTests
{
    private const string Draft = "等队友到齐 再开团。";
    private const string Ready = "可以说话了 · 测试麦克风";

    [Fact]
    public async Task PollAsync_LeftoverTransportFromAPreviousInstance_IsDeletedWithoutReplay()
    {
        await using var bridge = new Bridge();
        await bridge.WriteVerbatimAsync(bridge.Request());
        await File.WriteAllTextAsync(bridge.StatusPath, "leftover draft");

        await bridge.PollAsync();

        Assert.False(File.Exists(bridge.RequestPath));
        Assert.False(File.Exists(bridge.StatusPath));
        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_NoStartRequest_OpensNoMicrophone()
    {
        await using var bridge = new Bridge();

        await bridge.PollAsync();
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Theory]
    [InlineData("session", "old-game")]
    [InlineData("session", "")]
    public async Task PollAsync_RequestForAnotherGame_OpensNoMicrophone(string key, object value)
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        JObject request = bridge.Request();
        request[key] = JToken.FromObject(value);

        await bridge.WriteVerbatimAsync(request);
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Theory]
    [InlineData("updatedAt", -3001L)]
    [InlineData("createdAt", -60001L)]
    public async Task PollAsync_RequestOutsideItsLease_OpensNoMicrophone(string key, long offset)
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        JObject request = bridge.Request();
        request[key] = bridge.Now + offset;

        await bridge.WriteVerbatimAsync(request);
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Theory]
    [InlineData("updatedAt")]
    [InlineData("createdAt")]
    public async Task PollAsync_RequestFromTheFuture_OpensNoMicrophone(string key)
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        JObject request = bridge.Request();
        request[key] = bridge.Now + 1;

        await bridge.WriteVerbatimAsync(request);
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Theory]
    [InlineData("text", "cannot send")]
    [InlineData("confirmed", true)]
    public async Task PollAsync_RequestCarryingChatFields_OpensNoMicrophone(string key, object value)
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        JObject request = bridge.Request();
        request[key] = JToken.FromObject(value);

        await bridge.WriteVerbatimAsync(request);
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_RequestAuthorizingSending_OpensNoMicrophone()
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        JObject request = bridge.Request();
        request["action"] = "send";

        await bridge.WriteVerbatimAsync(request);
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Theory]
    [InlineData("protocol", "1")]
    [InlineData("protocol", 2)]
    [InlineData("id", "not-a-guid")]
    [InlineData("ack", "0")]
    public async Task PollAsync_MalformedRequest_OpensNoMicrophone(string key, object value)
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        JObject request = bridge.Request();
        request[key] = JToken.FromObject(value);

        await bridge.WriteVerbatimAsync(request);
        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_StartRequest_OpensExactlyOneRecognizer()
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        await bridge.WriteAsync(bridge.Request());

        await bridge.PollAsync();

        Assert.Equal(1, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_StartRequest_PublishesPreparing()
    {
        await using var bridge = new Bridge();

        await bridge.StartAsync();

        Assert.Equal("preparing", await bridge.StateAsync());
    }

    [Fact]
    public async Task PollAsync_SilentPreparationUpdate_IsNotReadiness()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("", false, Stage: "preparing"));
        await bridge.PollAsync();

        Assert.Equal("preparing", await bridge.StateAsync());
        Assert.Equal("", await bridge.HintAsync());
    }

    [Fact]
    public async Task PollAsync_EngineReportedRecording_IsReadiness()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("", false, Message: Ready));
        await bridge.PollAsync();

        Assert.Equal("recording", await bridge.StateAsync());
    }

    [Fact]
    public async Task PollAsync_Hypothesis_DoesNotReachTheDraft()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("临时假设", false));
        await bridge.PollAsync();

        Assert.Equal("", await bridge.TextAsync());
    }

    [Fact]
    public async Task PollAsync_FinalChunks_ReachTheDraftOnce()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("等队友到齐", true));
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("再开团。", true));
        await bridge.PollAsync();

        Assert.Equal(Draft, await bridge.TextAsync());
        Assert.Equal(1, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_DraftWhileRecording_IsReadableAndIncomplete()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("尚未确认", true));

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.Equal("尚未确认", await bridge.TextAsync());
        Assert.False(await bridge.CompleteAsync());
    }

    [Fact]
    public async Task PollAsync_AcknowledgingAnOlderSequence_KeepsTheNewerDraft()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("等队友到齐", true));
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("再开团。", true));
        await bridge.PollAsync();
        request["ack"] = 2;

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.Equal(3L, await bridge.SequenceAsync());
        Assert.Equal(Draft, await bridge.TextAsync());
    }

    [Fact]
    public async Task PollAsync_AcknowledgingTheCurrentSequence_RemovesTheDraft()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(Draft, true));
        await bridge.PollAsync();
        request["ack"] = await bridge.SequenceAsync();

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_StopRequest_PublishesTheFinalTextAsComplete()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(Draft, true));
        await bridge.PollAsync();
        request["action"] = "stop";

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.Equal(Draft, await bridge.TextAsync());
        Assert.True(await bridge.CompleteAsync(), "a stopped recording publishes its final text as complete");
    }

    [Fact]
    public async Task PollAsync_StopRequest_ReleasesTheMicrophone()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        request["action"] = "stop";

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.Equal(1, bridge.Sessions[0].Stops);
        Assert.True(bridge.Sessions[0].Disposed, "the recognizer is released once its worker has finished");
    }

    [Fact]
    public async Task PollAsync_AcknowledgingTheStoppedDraft_RemovesIt()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(Draft, true));
        request["action"] = "stop";
        await bridge.WriteAsync(request);
        await bridge.PollAsync();
        request["ack"] = await bridge.SequenceAsync();

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_ConsumedRequest_DoesNotReplayTheRecordingOrLateResults()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(Draft, true));
        await bridge.PollAsync();
        request["ack"] = await bridge.SequenceAsync();
        await bridge.WriteAsync(request);
        await bridge.PollAsync();
        File.Delete(bridge.RequestPath);
        bridge.Now += 4000;
        await bridge.PollAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("迟到内容", true));
        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.Equal(1, bridge.Sessions.Count);
        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_NewRequestAfterTheRecordingEnded_StartsANewSession()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();
        bridge.Sessions[0].Complete(new VoiceRecognitionOutcome(false));

        await bridge.WriteAsync(bridge.Request());
        await bridge.PollAsync();

        Assert.Equal(2, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_StaleLeaseWithAnUnreadableRequest_CancelsTheAudioAndClearsTheDraft()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("尚未确认", true));
        await bridge.WriteAsync(request);
        await bridge.PollAsync();
        await File.WriteAllTextAsync(bridge.RequestPath, "{incomplete request");

        bridge.Now += 3100;
        await bridge.PollAsync();

        Assert.True(bridge.Sessions[0].Cancelled, "a widget that stopped heartbeating releases the microphone");
        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_ClickAfterACancellation_StartsANewRecognizer()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        request["action"] = "cancel";
        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        await bridge.WriteAsync(bridge.Request());
        await bridge.PollAsync();

        Assert.Equal(2, bridge.Sessions.Count);
        Assert.Equal("preparing", await bridge.StateAsync());
    }

    [Fact]
    public async Task PollAsync_MatchChange_CancelsTheAudioAndClearsTheDraft()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Session = "next-game";
        await bridge.PollAsync();

        Assert.True(bridge.Sessions[0].Cancelled, "a new match must not keep the previous microphone open");
        Assert.False(File.Exists(bridge.RequestPath));
        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_DisabledBackend_DoesNotStartAudio()
    {
        await using var bridge = new Bridge();
        await bridge.PollAsync(enabled: false);
        await bridge.WriteAsync(bridge.Request());

        await bridge.PollAsync(enabled: false);

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_CancelWithoutAStart_DoesNotOpenAudio()
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        await bridge.WriteAsync(bridge.Request(action: "cancel"));

        await bridge.PollAsync();

        Assert.Equal(0, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_ReEnabledBackend_ResumesRecording()
    {
        await using var bridge = new Bridge();
        await bridge.PollAsync(enabled: false);
        JObject request = bridge.Request();
        await bridge.WriteAsync(request);
        await bridge.PollAsync(enabled: false);
        await bridge.PollAsync();
        await bridge.WriteAsync(request);

        await bridge.PollAsync();

        Assert.Equal(1, bridge.Sessions.Count);
        Assert.Equal("preparing", await bridge.StateAsync());
    }

    [Fact]
    public async Task PollAsync_ClickAfterAFailure_StartsANewRecognizer()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();
        bridge.Sessions[0].Fail(new UnauthorizedAccessException());
        await bridge.PollAsync();

        await bridge.WriteAsync(bridge.Request());
        await bridge.PollAsync();

        Assert.Equal(2, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_CancelRequest_ReleasesTheMicrophone()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        request["action"] = "cancel";

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.True(bridge.Sessions[0].Cancelled, "an explicit cancel request releases the microphone");
    }

    [Fact]
    public async Task PollAsync_CancelRequest_ClearsTheDraft()
    {
        await using var bridge = new Bridge();
        JObject request = await bridge.StartAsync();
        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("尚未确认", true));
        await bridge.WriteAsync(request);
        await bridge.PollAsync();
        request["action"] = "cancel";

        await bridge.WriteAsync(request);
        await bridge.PollAsync();

        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_ReplacementWhileTheRecordingIsReleasing_IsNotAccepted()
    {
        await using var bridge = new Bridge { Hold = true };
        await bridge.StartAsync();

        await bridge.WriteAsync(bridge.Request());
        await bridge.PollAsync();

        Assert.Equal(1, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_FinalChunkWithoutWords_PublishesNoText()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("", true));
        await bridge.PollAsync();

        Assert.Equal("", await bridge.TextAsync());
    }

    [Fact]
    public async Task PollAsync_FinalChunkWithoutWords_DoesNotStopTheMicrophone()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("", true));

        Assert.Equal(0, bridge.Sessions[0].Stops);
    }

    [Fact]
    public async Task PollAsync_DraftReachingTheLimit_StopsTheMicrophone()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(new string('长', 200), true));

        Assert.Equal(1, bridge.Sessions[0].Stops);
    }

    [Fact]
    public async Task PollAsync_OversizedDraft_IsPublishedCutToTheLimit()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(new string('长', 200), true));
        await bridge.PollAsync();

        Assert.Equal(new string('长', 180), await bridge.TextAsync());
    }

    [Fact]
    public async Task PollAsync_DraftCutThroughASurrogatePair_DropsTheSplitCharacter()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(new string('a', 179) + "\U0001F600", true));
        await bridge.PollAsync();

        Assert.Equal(new string('a', 179), await bridge.TextAsync());
    }

    [Theory]
    [InlineData("甲\u2028乙\u2029丙", "甲 乙 丙")]
    [InlineData("甲\u0001乙", "甲 乙")]
    [InlineData("甲\t乙", "甲 乙")]
    public async Task PollAsync_ControlAndSeparatorCharacters_ReachTheDraftAsSpaces(string chunk, string expected)
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(chunk, true));
        await bridge.PollAsync();

        Assert.Equal(expected, await bridge.TextAsync());
    }

    [Fact]
    public async Task PollAsync_RequestPathThatCannotBeCleaned_StillClearsTheDraft()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();
        Assert.True(File.Exists(bridge.StatusPath), "the recording left a draft in the transport");
        File.Delete(bridge.RequestPath);
        Directory.CreateDirectory(bridge.RequestPath);

        bridge.Session = "next-game";
        await bridge.PollAsync();

        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_TransportRepairedAfterAFailedCleanup_StartsANewRecording()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();
        File.Delete(bridge.RequestPath);
        Directory.CreateDirectory(bridge.RequestPath);
        bridge.Session = "next-game";
        await bridge.PollAsync();
        Directory.Delete(bridge.RequestPath);

        await bridge.WriteAsync(bridge.Request());
        await bridge.PollAsync();

        Assert.Equal(2, bridge.Sessions.Count);
        Assert.Equal("preparing", await bridge.StateAsync());
    }

    [Fact]
    public async Task PollAsync_StatusThatCannotBePublished_IsRetriedOnTheNextPoll()
    {
        await using var bridge = new Bridge();
        await bridge.SettleAsync();
        Directory.CreateDirectory(bridge.StatusPath);

        await bridge.WriteAsync(bridge.Request());
        await bridge.PollAsync();

        Assert.Equal(1, bridge.Sessions.Count);
        Assert.False(File.Exists(bridge.TemporaryStatusPath));
        Directory.Delete(bridge.StatusPath);
        await bridge.PollAsync();

        Assert.Equal("preparing", await bridge.StateAsync());
    }

    [Fact]
    public async Task PollAsync_TransportPathTheFileSystemRefuses_IsRetriedOnTheNextPoll()
    {
        await using var bridge = new Bridge();

        await bridge.PollAsync(Path.Combine(bridge.Root, "bad<>|name"));
        await bridge.StartAsync();

        Assert.Equal(1, bridge.Sessions.Count);
    }

    [Fact]
    public async Task PollAsync_LateUpdateAfterShutdown_DoesNotRestartTheMicrophone()
    {
        await using var bridge = new Bridge { Hold = true };
        await bridge.StartAsync();
        bridge.Service.Dispose();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate("关停后的迟到内容", true));

        Assert.Equal(0, bridge.Sessions[0].Stops);
    }

    [Fact]
    public async Task Dispose_DuringRecording_ReleasesTheMicrophoneAndCleansTheTransport()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Service.Dispose();

        Assert.True(bridge.Sessions[0].Cancelled, "shutdown releases the microphone");
        Assert.False(File.Exists(bridge.RequestPath));
        Assert.False(File.Exists(bridge.StatusPath));
    }

    [Fact]
    public async Task PollAsync_Recording_NeverWritesAChatRequest()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Emit(new VoiceRecognitionUpdate(Draft, true));
        await bridge.PollAsync();

        Assert.True(File.Exists(bridge.StatusPath), "the bridge published its own status");
        Assert.False(File.Exists(bridge.Transport("chat-request.json")));
    }

    [Fact]
    public async Task PollAsync_ResultMessage_IsReportedAsTheHint()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(
            bridge,
            session => session.Complete(new VoiceRecognitionOutcome(false, "检查用结果提示"))
        );

        Assert.Equal("检查用结果提示", hint);
    }

    [Fact]
    public async Task PollAsync_SelfCancelledRecognition_IsExplained()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(
            bridge,
            session => session.Complete(new VoiceRecognitionOutcome(true))
        );

        Assert.Contains("取消", hint);
    }

    [Fact]
    public async Task PollAsync_TimedOutRecognition_IsExplained()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(bridge, session => session.Fail(new OperationCanceledException()));

        Assert.Contains("超时", hint);
    }

    [Fact]
    public async Task PollAsync_VoiceFailure_IsReportedVerbatim()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(
            bridge,
            session => session.Fail(new VoiceRecognitionException("检查用语音失败"))
        );

        Assert.Equal("检查用语音失败", hint);
    }

    [Fact]
    public async Task PollAsync_MissingVoiceEngine_IsExplained()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(
            bridge,
            session => session.Fail(new InvalidOperationException("当前没有可用的中文离线语音引擎（检查用）"))
        );

        Assert.Contains("引擎", hint);
    }

    [Fact]
    public async Task PollAsync_DeniedMicrophone_ExplainsTheWindowsSetting()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(bridge, session => session.Fail(new UnauthorizedAccessException()));

        Assert.Contains("麦克风", hint);
    }

    [Fact]
    public async Task PollAsync_UnexpectedFailure_ReportsTheGenericAdvice()
    {
        await using var bridge = new Bridge();

        string hint = await HintAfterFinishingAsync(bridge, session => session.Fail(new IOException("检查用失败")));

        Assert.Contains("本机识别失败", hint);
    }

    [Fact]
    public async Task PollAsync_RecognitionFailure_PublishesTheErrorStateAndUnlocksTheEditor()
    {
        await using var bridge = new Bridge();
        await bridge.StartAsync();

        bridge.Sessions[0].Fail(new UnauthorizedAccessException());
        await bridge.PollAsync();

        Assert.Equal("error", await bridge.StateAsync());
        Assert.True(await bridge.CompleteAsync(), "a failed recording unlocks the editor");
    }

    private static async Task<string> HintAfterFinishingAsync(Bridge bridge, Action<StubSession> finish)
    {
        await bridge.StartAsync();
        finish(bridge.Sessions[^1]);
        await bridge.PollAsync();
        return await bridge.HintAsync();
    }

    /// <summary>
    /// One recognition bridge over its own temporary transport directory, fed by a stub recognizer factory. The stub
    /// completes its result without posting the continuation, so the bridge's cleanup runs on the thread that calls
    /// it: an assertion after a stop, a failure or a cancel sees the finished state, and no test has to wait.
    /// </summary>
    private sealed class Bridge : IAsyncDisposable
    {
        private readonly TempFolder _folder = new("league-voice-bridge");
        private readonly List<StubSession> _sessions = new();

        public Bridge()
        {
            Service = new GameBarVoiceRecognition(() =>
            {
                var session = new StubSession(Hold);
                _sessions.Add(session);
                return session;
            });
        }

        public GameBarVoiceRecognition Service { get; }

        public long Now { get; set; } = 100000;

        public string Session { get; set; } = "main-instance:game/3140";

        /// <summary>Recognizers that ignore cancellation, so a replaced recording can still be in flight.</summary>
        public bool Hold { get; set; }

        public IReadOnlyList<StubSession> Sessions => _sessions;

        public string Root => _folder.Root;

        public string RequestPath => _folder.File("voice-request.json");

        public string StatusPath => _folder.File("voice-status.json");

        public string TemporaryStatusPath => _folder.File("voice-status.tmp");

        public string Transport(string name) => _folder.File(name);

        /// <summary>The first poll of a session only settles it and clears what a previous instance left behind.</summary>
        public Task SettleAsync() => PollAsync();

        public Task PollAsync(bool enabled = true) => Service.PollAsync(Root, Session, enabled, time: Now);

        public Task PollAsync(string directory) => Service.PollAsync(directory, Session, true, time: Now);

        /// <summary>Settles the session, writes a start request and polls once; that poll accepts the request.</summary>
        public async Task<JObject> StartAsync()
        {
            await SettleAsync();
            JObject request = Request();
            await WriteAsync(request);
            await PollAsync();
            return request;
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

        /// <summary>Writes the request with a fresh lease, as the widget's own heartbeat would.</summary>
        public Task WriteAsync(JObject request)
        {
            request["updatedAt"] = Now;
            return WriteVerbatimAsync(request);
        }

        /// <summary>Writes the request exactly as given, so a test can forge a stale or future lease.</summary>
        public Task WriteVerbatimAsync(JObject request) => File.WriteAllTextAsync(RequestPath, request.ToString());

        public async Task<JObject?> StatusAsync() =>
            File.Exists(StatusPath) ? JObject.Parse(await File.ReadAllTextAsync(StatusPath)) : null;

        public async Task<string?> StateAsync() => (string?)(await StatusAsync())?["state"];

        public async Task<string> TextAsync() => (string?)(await StatusAsync())?["text"] ?? "";

        public async Task<string> HintAsync() => (string?)(await StatusAsync())?["hint"] ?? "";

        public async Task<bool> CompleteAsync() => ((bool?)(await StatusAsync())?["complete"]).GetValueOrDefault();

        public async Task<long> SequenceAsync() => ((long?)(await StatusAsync())?["sequence"]).GetValueOrDefault();

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            // A recognizer held past cancellation still owns an in-flight recording; release it before the transport
            // directory goes away.
            foreach (StubSession session in _sessions)
            {
                session.Release();
            }

            await Task.WhenAll(_sessions.Select(session => session.Finished.Task));
            _folder.Dispose();
        }
    }

    /// <summary>A recognizer whose readiness, results and failures are all decided by the test.</summary>
    private sealed class StubSession : IDesktopVoiceSession
    {
        private readonly TaskCompletionSource<VoiceRecognitionOutcome> _result = new();
        private readonly bool _holdResult;

        public StubSession(bool holdResult) => _holdResult = holdResult;

        public event Action<VoiceRecognitionUpdate>? Update;

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public bool Disposed { get; private set; }

        public int Stops { get; private set; }

        public void Emit(VoiceRecognitionUpdate update) => Update?.Invoke(update);

        public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
        {
            if (!_holdResult)
            {
                // The callback only records the cancellation. Completing the result from inside it would run the
                // bridge's cleanup while the token source is still cancelling, so the follow-up is left to Dispose,
                // which the bridge calls immediately afterwards.
                token.Register(() => Cancelled = true);
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

        public void Release() => _result.TrySetResult(new VoiceRecognitionOutcome(false));

        public void Dispose()
        {
            Disposed = true;
            if (!_holdResult && !_result.Task.IsCompleted)
            {
                _result.TrySetResult(new VoiceRecognitionOutcome(true));
            }
            Finished.TrySetResult();
        }
    }
}
