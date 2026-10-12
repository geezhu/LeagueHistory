using System.Net;
using System.Reflection;
using System.Speech.Synthesis;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class AssistantVoiceChecks
{
    private static SynchronizationContext? _ui;

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        Task.Run(ChatChecks).GetAwaiter().GetResult();
        WinFormsCheckHost.Run(() =>
        {
            _ui = SynchronizationContext.Current;
            DialogChecks();
            FooterCheck();
        });
        Console.WriteLine(
            "PASS: assistant voice draft/confirmation, stop/cancel/late callbacks, one send per click, footer fit, strict room/phase binding and chat rejection. No microphone or real chat used."
        );
    }

    private static async Task ChatChecks()
    {
        using var fixture = new ChatFixture();
        string capture = await ConfirmedChatAsync(fixture);
        await RejectedChatAsync(fixture, capture);
    }

    private static async Task<string> ConfirmedChatAsync(ChatFixture fixture)
    {
        string capture = await ChampionSelectVoiceChatService.CaptureRoomAsync(fixture.Client, CancellationToken.None);
        Check(capture == fixture.Room && fixture.Posts == 0, "Opening or recording is read-only");
        await ChampionSelectVoiceChatService.SendAsync(
            fixture.Client,
            capture,
            "  等队友到齐再开团。  ",
            CancellationToken.None
        );
        Check(
            fixture.Posts == 1 && fixture.Posted == "等队友到齐再开团。",
            "Only the confirmed trimmed text reaches champion-select chat"
        );
        return capture;
    }

    private static async Task RejectedChatAsync(ChatFixture fixture, string capture)
    {
        foreach (string invalid in new[] { " ", "一行\n另一行", "/ff", new string('字', 181) })
        {
            int before = fixture.Reads;
            await fixture.Rejected(
                () =>
                    ChampionSelectVoiceChatService.SendAsync(fixture.Client, capture, invalid, CancellationToken.None),
                "Invalid text cannot POST"
            );
            Check(fixture.Reads == before, "Invalid text cannot even query chat rooms");
        }
        fixture.Room = "room/two";
        await fixture.Rejected(
            () => ChampionSelectVoiceChatService.SendAsync(fixture.Client, capture, "草稿", CancellationToken.None),
            "A new room cannot receive an old draft"
        );
        fixture.Room = capture;
        fixture.Phase = "InProgress";
        await fixture.Rejected(
            () => ChampionSelectVoiceChatService.SendAsync(fixture.Client, capture, "草稿", CancellationToken.None),
            "In-game phase cannot receive champion-select voice text"
        );
        fixture.Phase = "ChampSelect";
        await fixture.Rejected(
            () =>
                ChampionSelectVoiceChatService.SendAsync(
                    fixture.Client,
                    capture,
                    "草稿",
                    CancellationToken.None,
                    () => false
                ),
            "Disposed or hidden owner cannot POST"
        );
        fixture.ChangeAfterRooms = true;
        await fixture.Rejected(
            () => ChampionSelectVoiceChatService.SendAsync(fixture.Client, capture, "草稿", CancellationToken.None),
            "Leaving selection while discovering a room cancels POST"
        );
        fixture.ChangeAfterRooms = false;
        fixture.Phase = "ChampSelect";
        fixture.Ambiguous = true;
        await fixture.Rejected(
            () => ChampionSelectVoiceChatService.SendAsync(fixture.Client, capture, "草稿", CancellationToken.None),
            "Conflicting champion-select rooms are not guessed"
        );
        fixture.Ambiguous = false;
        fixture.Reject = true;
        int count = fixture.Posts;
        bool failed = false;
        try
        {
            await ChampionSelectVoiceChatService.SendAsync(
                fixture.Client,
                capture,
                "被拒绝的消息",
                CancellationToken.None
            );
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Check(failed && fixture.Posts == count + 1, "HTTP rejection is reported and never automatically retried");
    }

    private sealed class ChatFixture : IDisposable
    {
        public string Phase = "ChampSelect",
            Room = "room/one",
            Posted = "";
        public int Posts,
            Reads;
        public bool Reject,
            ChangeAfterRooms,
            Ambiguous;
        public HttpClient Client { get; }

        public ChatFixture()
        {
            Client = new HttpClient(
                new Handler(request =>
                {
                    string path = request.RequestUri!.AbsolutePath;
                    if (request.Method == HttpMethod.Post)
                    {
                        Posts++;
                        Check(
                            path == "/lol-chat/v1/conversations/room%2Fone/messages",
                            "POST must bind to the exact captured room, with its id escaped"
                        );
                        Posted =
                            (string?)
                                JObject.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())["body"]
                            ?? "";
                        return new(Reject ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
                    }
                    Reads++;
                    if (path.EndsWith("gameflow-phase"))
                    {
                        return Json(new JValue(Phase));
                    }

                    if (path.EndsWith("conversations"))
                    {
                        if (ChangeAfterRooms)
                        {
                            Phase = "InProgress";
                        }

                        var rooms = new JArray(
                            new JObject { ["id"] = "private", ["type"] = "chat" },
                            new JObject { ["id"] = Room, ["type"] = "championSelect" }
                        );
                        if (Ambiguous)
                        {
                            rooms.Add(new JObject { ["id"] = "old-room", ["type"] = "champSelect" });
                        }

                        return Json(rooms);
                    }
                    throw new Exception("Unexpected voice chat endpoint");
                })
            )
            {
                BaseAddress = new Uri("https://fixture.invalid"),
            };
        }

        public async Task Rejected(Func<Task> action, string note)
        {
            int before = Posts;
            bool refused = false;
            try
            {
                await action();
            }
            catch (Exception e) when (e is InvalidOperationException or OperationCanceledException)
            {
                refused = true;
            }
            Check(refused && Posts == before, note);
        }

        public void Dispose() => Client.Dispose();
    }

    private static void DialogChecks()
    {
        var sessions = new List<FakeSession>();
        int sends = 0;
        string sent = "";
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<IDesktopVoiceSession> factory = () =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        };
        var type = typeof(League.FormMain).Assembly.GetType("League.VoiceInputDialog")!;
        using var dialog = (Form)
            Activator.CreateInstance(
                type,
                new object[]
                {
                    (Func<string, CancellationToken, Task>)(
                        (text, _) =>
                        {
                            sends++;
                            sent = text;
                            return sending.Task;
                        }
                    ),
                    (Func<bool>)(() => true),
                    factory,
                }
            )!;
        dialog.Show();
        Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(_ui);
        var draft = (TextBox)dialog.Controls.Find("voiceDraft", true).Single();
        var record = (Button)dialog.Controls.Find("voiceRecord", true).Single();
        var send = (Button)dialog.Controls.Find("voiceSend", true).Single();
        var status = (Label)dialog.Controls.Find("voiceStatus", true).Single();
        Check(sessions.Count == 0 && !send.Enabled, "Opening never starts a microphone or sends an empty draft");
        Check(status.Text.Length == 0, "Opening contains no engine-loading instructions");
        draft.Text = "手动输入";
        Check(send.Enabled && sends == 0, "Editing alone cannot send");
        RecordAndDraft(draft, record, send, status, sessions, () => sends);
        ConfirmAndClose(dialog, draft, record, send, status, sessions, sending, () => sends, () => sent);
        ImmediateFinalPreview(factory, sessions);
        StartWhenShownAndEnterToSend(factory, sessions);
    }

    private static void RecordAndDraft(
        TextBox draft,
        Button record,
        Button send,
        Label status,
        List<FakeSession> sessions,
        Func<int> sendCount
    )
    {
        record.PerformClick();
        Check(sessions.Count == 1 && draft.ReadOnly && !send.Enabled, "Recording locks confirmation and editing");
        var first = sessions[0];
        Check(
            !record.Enabled && record.AccessibleName.Contains("准备"),
            "Recording is not offered before microphone readiness"
        );
        first.Emit(new("", false, Stage: "preparing", Message: "模型内部加载过程"));
        Application.DoEvents();
        Check(
            !record.Enabled && status.Text == "",
            "Model preparation remains a button state without extra explanation"
        );
        first.Emit(new("", false, Message: "可以说话了 · 测试麦克风"));
        Pump(() => record.Enabled);
        Check(status.Text.Contains("可以说话了"), "Only a ready microphone prompts the user to speak");
        first.Emit(new("假设文字", false));
        Application.DoEvents();
        Check(
            draft.TextLength == 0 && sendCount() == 0 && !status.Text.Contains("假设文字"),
            "Hypotheses are neither draft text nor redundant status text"
        );
        first.Emit(new("等队友到齐", true));
        Pump(() => draft.Text == "等队友到齐");
        first.Emit(new("再开团。", true));
        Pump(() => draft.Text.EndsWith("再开团。"));
        record.PerformClick();
        Pump(() => !draft.ReadOnly);
        Check(
            first.Stopped && first.Disposed && send.Enabled && sendCount() == 0,
            "Stopping releases the recognizer and keeps the editable draft unsent"
        );
    }

    private static void ConfirmAndClose(
        Form dialog,
        TextBox draft,
        Button record,
        Button send,
        Label status,
        List<FakeSession> sessions,
        TaskCompletionSource sending,
        Func<int> sendCount,
        Func<string> sentText
    )
    {
        draft.Text = "检查后修改的文字";
        send.PerformClick();
        send.PerformClick();
        Check(
            sendCount() == 1 && sentText() == draft.Text && draft.ReadOnly,
            "Two rapid clicks cannot duplicate a confirmed send"
        );
        sending.SetResult();
        Pump(() => draft.TextLength == 0);
        Check(status.Text.Contains("已发送"), "Successful confirmation clears the sent draft");
        draft.Text = "/ff";
        Check(!send.Enabled, "Slash commands cannot be confirmed");
        draft.Text = "一行\n另一行";
        Check(!send.Enabled, "Pasted multi-line text cannot be confirmed");
        record.PerformClick();
        var second = sessions[1];
        dialog.Dispose();
        second.Emit(new("关闭后到达的文字", true));
        var cleanup = (Task)
            dialog
                .GetType()
                .GetField("_cleanupWork", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
        Pump(() => second.Disposed && cleanup.IsCompleted);
        cleanup.GetAwaiter().GetResult();
        Check(
            second.Cancelled && sendCount() == 1,
            "Closing cancels recording, releases resources and ignores late callbacks"
        );
    }

    private static void StartWhenShownAndEnterToSend(Func<IDesktopVoiceSession> factory, List<FakeSession> sessions)
    {
        var type = typeof(League.FormMain).Assembly.GetType("League.VoiceInputDialog")!;
        int sends = 0;
        using var dialog = (Form)
            Activator.CreateInstance(
                type,
                new object[]
                {
                    (Func<string, CancellationToken, Task>)(
                        (_, _) =>
                        {
                            sends++;
                            return Task.CompletedTask;
                        }
                    ),
                    (Func<bool>)(() => true),
                    factory,
                }
            )!;
        int before = sessions.Count;
        type.GetMethod("StartRecordingWhenShown")!.Invoke(dialog, null);
        dialog.Show();
        Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(_ui);
        Check(sessions.Count == before + 1, "The assistant microphone button starts recording when the dialog opens");
        var session = sessions[^1];
        session.Emit(new("先打龙", true, Stage: "done"));
        session.Stop();
        var draft = (TextBox)dialog.Controls.Find("voiceDraft", true).Single();
        Pump(() => !draft.ReadOnly && draft.Text == "先打龙");
        typeof(Control)
            .GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(draft, new object[] { new KeyEventArgs(Keys.Enter) });
        Pump(() => sends == 1);
        Check(sends == 1 && !draft.Text.Contains('\n'), "Enter confirms the draft once without inserting a line break");
    }

    private static void ImmediateFinalPreview(Func<IDesktopVoiceSession> factory, List<FakeSession> sessions)
    {
        var type = typeof(League.FormMain).Assembly.GetType("League.VoiceInputDialog")!;
        string folder = Path.Combine(AppContext.BaseDirectory, "AssistantVoicePreview");
        Directory.CreateDirectory(folder);
        using var preview = (Form)
            Activator.CreateInstance(
                type,
                new object[]
                {
                    (Func<string, CancellationToken, Task>)((_, _) => Task.CompletedTask),
                    (Func<bool>)(() => true),
                    factory,
                }
            )!;
        preview.Show();
        Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(_ui);
        preview.PerformLayout();
        var text = (TextBox)preview.Controls.Find("voiceDraft", true).Single();
        text.Text = "等队友到齐再开团，注意对面绕后。";
        using var image = new Bitmap(preview.Width, preview.Height);
        preview.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(folder, "voice-input.png"));
        var quickRecord = (Button)preview.Controls.Find("voiceRecord", true).Single();
        quickRecord.PerformClick();
        var instant = sessions[^1];
        instant.Emit(new("停止时到达的最终文字", true, float.NaN, Stage: "done"));
        instant.Stop();
        Pump(() => !text.ReadOnly);
        Check(
            text.Text == "停止时到达的最终文字",
            "Final callback is drained even when the recognition task completes immediately"
        );
    }

    private static void FooterCheck()
    {
        using var owner = new League.FormMain();
        var type = typeof(League.FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        using var assistant = (Form)Activator.CreateInstance(type, new object[] { owner })!;
        (
            (System.Windows.Forms.Timer)
                type.GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(assistant)!
        ).Stop();
        assistant.ClientSize = new Size(320, 590);
        assistant.Show();
        Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(_ui);
        assistant.PerformLayout();
        var footer = assistant.Controls.OfType<FlowLayoutPanel>().Single(p => p.Dock == DockStyle.Bottom);
        footer.PerformLayout();
        Check(
            footer.Controls.Count == 3
                && footer
                    .Controls.Cast<Control>()
                    .All(c =>
                        c.Top >= 0
                        && c.Bottom <= footer.ClientSize.Height
                        && c.Right <= footer.ClientSize.Width - footer.Padding.Right
                    ),
            "Guide, rune and voice buttons fit on one row; retry lives in the status bar"
        );
        using var image = new Bitmap(assistant.Width, assistant.Height);
        assistant.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(AppContext.BaseDirectory, "AssistantVoicePreview", "assistant-footer.png"));
    }

    public static void EngineCheck()
    {
        using var engine = DesktopVoiceSession.CreateChineseEngine();
        Check(engine.RecognizerInfo.Culture.Name.StartsWith("zh"), "The actual local dictation engine is Chinese");
        using var synth = new SpeechSynthesizer();
        var voice = synth.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name == "zh-CN");
        if (voice == null)
        {
            Console.WriteLine(
                "PASS: Chinese dictation grammar loads; no Chinese synthesis voice for the WAV check. No microphone opened."
            );
            return;
        }
        using var wave = new MemoryStream();
        synth.SelectVoice(voice.VoiceInfo.Name);
        synth.SetOutputToWaveStream(wave);
        synth.Speak("你好队友，我们一起出发。");
        synth.SetOutputToNull();
        wave.Position = 0;
        using var session = new DesktopVoiceSession(recognizer => recognizer.SetInputToWaveStream(wave));
        string recognized = "";
        session.Update += update =>
        {
            if (update.Final)
            {
                recognized += update.Text;
            }
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var outcome = session.RunAsync(timeout.Token).GetAwaiter().GetResult();
        Check(
            !outcome.Cancelled && recognized.Any(c => c is >= '\u4e00' and <= '\u9fff'),
            "The production async recognition session recognizes synthesized Chinese from an in-memory WAV"
        );
        Console.WriteLine(
            "PASS: real Chinese offline dictation and in-memory synthesized WAV recognition. No microphone, speaker playback or game chat used."
        );
    }

    private static void Pump(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(_ui);
            Thread.Sleep(5);
        }
        Check(condition(), "UI continuation timed out");
        Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(_ui);
    }

    private static HttpResponseMessage Json(JToken value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString(Newtonsoft.Json.Formatting.None)) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(handle(request));
    }

    private sealed class FakeSession : IDesktopVoiceSession
    {
        public event Action<VoiceRecognitionUpdate>? Update;
        private readonly TaskCompletionSource<VoiceRecognitionOutcome> _done = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public bool Stopped,
            Disposed,
            Cancelled;

        public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
        {
            token.Register(() =>
            {
                Cancelled = true;
                _done.TrySetCanceled(token);
            });
            return _done.Task;
        }

        public void Emit(VoiceRecognitionUpdate value) => Update?.Invoke(value);

        public void Stop()
        {
            Stopped = true;
            _done.TrySetResult(new(false));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
