using System.Collections.Concurrent;
using System.Reflection;
using League.Services;
using static StatisticsCheckSupport;

/// <summary>
/// The champion-select voice draft dialog: draft validation, the recording state machine driven by a stub voice
/// session, recognition outcomes and failures, sending, and closing. Every dialog is built with its own session
/// factory, so no microphone, recognizer or real chat is ever involved.
/// </summary>
internal static class VoiceInputDialogChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type DialogType = typeof(League.FormMain).Assembly.GetType("League.VoiceInputDialog")!;
    private static SynchronizationContext? _ui;

    public static void Run()
    {
        _ui = SynchronizationContext.Current;
        Validation();
        DefaultFactory();
        Keyboard();
        StartWhenShown();
        Stages();
        Finals();
        Limit();
        Guards();
        Outcomes();
        Failures();
        Sending();
        SendFailures();
        QueuedWork();
        Closing();
        FaultedCleanup();
        Console.WriteLine(
            "PASS: the voice dialog validates its draft, follows the recording, decoding and stopping stages, "
                + "appends and clips finals, reports every recognition outcome and failure, sends once and closes "
                + "its session without a microphone or real chat."
        );
    }

    private static void Validation()
    {
        using var fixture = new Fixture();
        Check(fixture.Sessions.Count == 0 && !fixture.Send.Enabled, "Opening the dialog starts no microphone");
        Check(fixture.Status.Text.Length == 0, "Opening shows no engine or validation text");
        Check(fixture.Record.Enabled && fixture.Record.AccessibleName == "开始录音", "An empty draft offers recording");
        Check(fixture.Count.Text == "0 / 180", "The length counter starts at zero");

        fixture.Draft.Text = "手动输入";
        Check(
            fixture.Send.Enabled && fixture.Record.AccessibleName == "重新录音" && fixture.Count.Text == "4 / 180",
            "A valid draft is confirmable and the counter follows it"
        );
        fixture.Draft.Text = "/ff";
        Check(
            !fixture.Send.Enabled && fixture.Status.Text.Contains("不支持斜杠命令"),
            "A slash command is refused before sending"
        );
        fixture.Draft.Text = "一行\n另一行";
        Check(!fixture.Send.Enabled && fixture.Status.Text.Contains("单行聊天文字"), "A pasted line break is refused");
        fixture.Draft.Text = new string('字', 181);
        Check(!fixture.Send.Enabled && fixture.Count.Text == "181 / 180", "Text past the limit is refused");
        fixture.Draft.Clear();
        Check(!fixture.Send.Enabled && fixture.Status.Text.Length == 0, "Clearing the draft removes the warning");

        fixture.CurrentAllowed = false;
        fixture.Draft.Text = "仍未连接";
        Check(!fixture.Record.Enabled && !fixture.Send.Enabled, "A dialog that is no longer current offers nothing");
    }

    private static void DefaultFactory()
    {
        using var dialog = (Form)
            Activator.CreateInstance(
                DialogType,
                new object[]
                {
                    (Func<string, CancellationToken, Task>)((_, _) => Task.CompletedTask),
                    (Func<bool>)(() => true),
                    null!,
                }
            )!;
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-30000, -30000);
        dialog.Show();
        Application.DoEvents();
        Check(
            Field<Button>(dialog, "_record").Enabled,
            "Without a supplied factory the dialog still offers recording until it is clicked"
        );
    }

    private static void Keyboard()
    {
        int sends = 0;
        using var fixture = new Fixture(
            send: (_, _) =>
            {
                sends++;
                return Task.CompletedTask;
            }
        );
        fixture.Draft.Text = "按回车发送";
        Key(fixture.Draft, Keys.Enter);
        fixture.Pump(() => sends == 1);
        Check(fixture.Draft.TextLength == 0, "Enter confirms the draft once without inserting a line break");
        fixture.Draft.Text = "/ff";
        Key(fixture.Draft, Keys.Enter);
        Check(sends == 1, "Enter cannot confirm a draft that validation refuses");

        using var closed = new Fixture();
        Key(closed.Dialog, Keys.A);
        Check(!closed.Dialog.IsDisposed, "A key the dialog does not use leaves it open");
        Key(closed.Dialog, Keys.Escape);
        closed.Pump(() => closed.Dialog.IsDisposed);
        Check(closed.Dialog.IsDisposed, "Escape closes the dialog");
    }

    private static void StartWhenShown()
    {
        using var armed = new Fixture(show: false);
        Invoke(armed.Dialog, "StartRecordingWhenShown");
        Check(Field<bool>(armed.Dialog, "_recordWhenShown"), "The assistant microphone button arms the dialog");
        armed.Show();
        armed.Pump(() => armed.Sessions.Count == 1);
        Check(armed.Draft.ReadOnly, "Showing an armed dialog starts recording without a click");
        armed.Last.Stop();
        armed.Pump(() => !armed.Draft.ReadOnly);

        using var stale = new Fixture(show: false);
        stale.CurrentAllowed = false;
        Invoke(stale.Dialog, "StartRecordingWhenShown");
        stale.Show();
        Application.DoEvents();
        Check(stale.Sessions.Count == 0, "A dialog that is no longer current does not start recording when shown");
        Check(Field<bool>(stale.Dialog, "_recordWhenShown"), "The armed flag is only consumed by an enabled button");
    }

    private static void Stages()
    {
        using var fixture = Recording();
        Check(
            fixture.Draft.ReadOnly && !fixture.Send.Enabled && !fixture.Record.Enabled,
            "While the session prepares, the draft is locked and recording is not offered again"
        );
        fixture.Last.Emit(new("", false, Stage: "preparing", Message: "模型内部加载过程"));
        fixture.Pump(() => fixture.Record.Text == "\uE712");
        Check(
            fixture.Status.Text.Length == 0 && fixture.Record.AccessibleName.Contains("准备"),
            "Preparing stays a button state without extra explanation"
        );

        fixture.Last.Emit(new("", false, Stage: "decoding", Message: "识别中"));
        fixture.Pump(() => fixture.Record.AccessibleName.Contains("识别中"));
        Check(fixture.Status.Text.Length == 0, "Decoding keeps the status line empty");

        fixture.Last.Emit(new("", false, Stage: "recording", Message: "可以说话了 · 测试麦克风"));
        fixture.Pump(() => fixture.Record.Text == "\uE71A");
        Check(
            fixture.Record.Enabled && fixture.Status.Text.Contains("可以说话了"),
            "Only a ready microphone offers the stop square and prompts the user to speak"
        );
        fixture.Last.Emit(new("假设文字", false));
        fixture.Last.Emit(new("", false, Stage: "recording", Message: "仍在听"));
        fixture.Pump(() => fixture.Status.Text == "仍在听");
        Check(fixture.Draft.TextLength == 0, "Hypotheses are never draft text");
    }

    private static void Finals()
    {
        using var fixture = Recording();
        fixture.Last.Emit(new("等队友到齐", true));
        fixture.Pump(() => fixture.Draft.Text == "等队友到齐");
        fixture.Last.Emit(new("再开团。", true, Confidence: .2f));
        fixture.Pump(() => fixture.Draft.Text.EndsWith("再开团。"));
        Check(fixture.Draft.Text == "等队友到齐 再开团。", "Finals append with one separating space");
        Check(fixture.Status.Text.Contains("请检查识别文字"), "A low-confidence final asks for a check");

        fixture.Last.Emit(new("换\u2028行", true));
        fixture.Pump(() => fixture.Draft.Text.Contains("换 行"));
        Check(!fixture.Draft.Text.Contains('\u2028'), "Line and paragraph separators become spaces");

        Check((string)Private(null, "Clip", "短文本", 6)! == "短文本", "Text inside the limit is kept");
        Check((string)Private(null, "Clip", new string('a', 6), 4)! == "aaaa", "Text past the limit is cut");
        string surrogate = new string('a', 3) + "😀";
        Check((string)Private(null, "Clip", surrogate, 4)! == "aaa", "A cut never leaves half of a surrogate pair");

        fixture.StopByButton();
        Check(
            fixture.Last.Stopped && fixture.Last.Disposed && fixture.Draft.Text == "等队友到齐 再开团。 换 行",
            "Stopping releases the recognizer and keeps the editable draft"
        );
        Check(fixture.Sessions.Count == 1, "Stopping does not open a second session");
    }

    private static void Limit()
    {
        using var fixture = Recording();
        fixture.Last.Emit(new(new string('长', 200), true));
        fixture.Pump(() => fixture.Draft.TextLength == 180);
        Check(fixture.Last.Stopped, "A full draft stops recording by itself instead of overflowing");
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        Check(fixture.Draft.TextLength == 180, "The stopped draft keeps exactly the limit");
    }

    private static void Guards()
    {
        using var fixture = Recording();
        Invoke(fixture.Dialog, "SendAsync");
        Invoke(fixture.Dialog, "RecordAsync");
        Check(fixture.Sessions.Count == 1, "Neither sending nor a second recording starts while recording");

        fixture.CurrentAllowed = false;
        fixture.Last.Stop();
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        Invoke(fixture.Dialog, "RecordAsync");
        Invoke(fixture.Dialog, "SendAsync");
        Invoke(fixture.Dialog, "StopRecording");
        Check(fixture.Sessions.Count == 1, "A dialog that is no longer current opens no session and sends nothing");
    }

    private static void Outcomes()
    {
        Outcome(new VoiceRecognitionOutcome(false, "识别服务返回的提示"), "识别服务返回的提示");
        Outcome(new VoiceRecognitionOutcome(true), "录音已取消。");
        Outcome(new VoiceRecognitionOutcome(false), "没有识别到文字，请重录或直接输入。");
        ImmediateOutcome();
    }

    private static void Outcome(VoiceRecognitionOutcome outcome, string expected)
    {
        using var fixture = Recording();
        fixture.Last.Finish(outcome);
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        Check(fixture.Status.Text == expected, "The outcome message is shown: " + fixture.Status.Text);
    }

    private static void ImmediateOutcome()
    {
        int sessions = 0;
        using var fixture = new Fixture(factory: () =>
        {
            sessions++;
            return new StubSession(new VoiceRecognitionOutcome(false, "立即结束"));
        });
        fixture.Record.PerformClick();
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        Check(
            sessions == 1 && fixture.Status.Text == "立即结束",
            "A session that ends at once still reports its message"
        );
    }

    private static void Failures()
    {
        Failure(new VoiceRecognitionException("语音模型缺失，请在设置中下载。"), "语音模型缺失，请在设置中下载。");
        Failure(new InvalidOperationException("当前没有可用的中文语音引擎。"), "当前没有可用的中文语音引擎。");
        Failure(
            new InvalidOperationException("别的错误"),
            "中文语音引擎或麦克风不可用，请检查语音组件和麦克风权限；也可直接输入。"
        );
        Failure(new Exception("boom"), "麦克风或本机中文模型不可用，请在设置中检查模型和麦克风权限，或直接输入。");
        CancelledRun();
        FactoryFailure();
    }

    private static void Failure(Exception error, string expected)
    {
        using var fixture = Recording();
        fixture.Last.Fail(error);
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        Check(fixture.Status.Text == expected, "A failed session explains itself: " + fixture.Status.Text);
        Check(fixture.Last.Disposed, "A failed session is released");
    }

    private static void CancelledRun()
    {
        using var fixture = Recording();
        fixture.Last.Fail(new OperationCanceledException());
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        Check(fixture.Status.Text == "录音已取消。", "Cancelling the recognition run reports a cancellation");

        // A dialog that stopped being current keeps the failure to itself instead of publishing a status.
        using var stale = Recording();
        stale.CurrentAllowed = false;
        stale.Last.Fail(new OperationCanceledException());
        stale.Pump(() => stale.Last.Disposed);
        Check(stale.Status.Text.Length == 0, "A cancellation is silent once the dialog stopped being current");

        using var rejected = Recording();
        rejected.CurrentAllowed = false;
        rejected.Last.Fail(new InvalidOperationException("麦克风被占用"));
        rejected.Pump(() => rejected.Last.Disposed);
        Check(rejected.Status.Text.Length == 0, "A failure is silent once the dialog stopped being current");
    }

    private static void FactoryFailure()
    {
        using var fixture = new Fixture(factory: () => throw new InvalidOperationException("当前没有可用的录音设备。"));
        fixture.Record.PerformClick();
        fixture.Pump(() => fixture.Record.Enabled);
        Check(
            fixture.Status.Text == "当前没有可用的录音设备。",
            "A session that cannot even be created is reported and the dialog stays usable"
        );
    }

    private static void Sending()
    {
        string sent = "";
        using var fixture = new Fixture(
            send: (text, _) =>
            {
                sent = text;
                return Task.CompletedTask;
            }
        );
        fixture.Record.PerformClick();
        fixture.Last.Emit(new(" 先手开团 ", true));
        fixture.Pump(() => fixture.Draft.Text.Contains("先手开团"));
        fixture.Last.Stop();
        fixture.Pump(() => !fixture.Draft.ReadOnly);
        fixture.Send.PerformClick();
        fixture.Pump(() => fixture.Draft.TextLength == 0);
        Check(sent == "先手开团", "Sending confirms the trimmed draft that recording produced");
        Check(fixture.Status.Text.Contains("已发送"), "A confirmed send is reported");

        fixture.Draft.Text = "/ff";
        Invoke(fixture.Dialog, "SendAsync");
        Check(
            fixture.Status.Text.Contains("不支持换行或斜杠命令"),
            "An invalid draft cannot be sent from the keyboard"
        );

        fixture.CurrentAllowed = false;
        fixture.Draft.Text = "仍然有效";
        Invoke(fixture.Dialog, "SendAsync");
        Check(fixture.Draft.Text == "仍然有效", "A stale dialog sends nothing");
    }

    private static void SendFailures()
    {
        SendFailure(_ => throw new OperationCanceledException(), "发送取消或超时");
        SendFailure(_ => throw new InvalidOperationException("客户端未连接。"), "客户端未连接。");
        SendFailure(_ => throw new Exception("boom"), "发送失败，请检查客户端聊天连接。");
    }

    private static void SendFailure(Action<string> send, string expected)
    {
        using var fixture = new Fixture(
            send: (text, _) =>
            {
                send(text);
                return Task.CompletedTask;
            }
        );
        fixture.Draft.Text = "草稿";
        fixture.Send.PerformClick();
        fixture.Pump(() => fixture.Send.Enabled);
        Check(fixture.Status.Text.Contains(expected), "A failed send is reported: " + fixture.Status.Text);
        Check(fixture.Draft.Text == "草稿", "A failed send never clears the draft");
    }

    private static void QueuedWork()
    {
        using var unshown = new Fixture(show: false);
        var untouched = new ConcurrentQueue<VoiceRecognitionUpdate>();
        untouched.Enqueue(new("没到过界面", true));
        Invoke(
            unshown.Dialog,
            "QueueUpdate",
            new VoiceRecognitionUpdate("没到过界面", true),
            untouched,
            (Action)(() => { })
        );
        Check(untouched.Count == 1, "A dialog without a handle queues no callback");
        using (var source = new CancellationTokenSource())
        {
            Invoke(unshown.Dialog, "DrainUpdates", 999, source, untouched);
        }
        Check(untouched.Count == 0, "Updates of an older generation are dropped");

        using var fixture = Recording();
        int generation = fixture.Generation;
        var staleFinal = new ConcurrentQueue<VoiceRecognitionUpdate>();
        staleFinal.Enqueue(new("过期", true));
        Invoke(fixture.Dialog, "DrainUpdates", generation + 1, fixture.Life!, staleFinal);
        Check(staleFinal.Count == 0 && fixture.Draft.TextLength == 0, "A stale generation cannot add text");

        fixture.StopByButton();
        var late = new ConcurrentQueue<VoiceRecognitionUpdate>();
        late.Enqueue(new("停止之后", true));
        using (var source = new CancellationTokenSource())
        {
            Invoke(fixture.Dialog, "DrainUpdates", generation, source, late);
        }
        Check(late.Count == 0 && fixture.Draft.TextLength == 0, "Updates drained after stopping add no text");
    }

    private static void Closing()
    {
        var fixture = new Fixture();
        int changes = 0;
        fixture.Draft.TextChanged += (_, _) => changes++;
        fixture.Record.PerformClick();
        var session = fixture.Last;
        fixture.Dialog.Dispose();
        int settled = changes;
        Check(session.Cancelled && session.Disposed, "Closing cancels and releases the recording session");
        session.Emit(new("关闭后到达的文字", true));
        fixture.Pump(() => fixture.CleanupCompleted);
        Check(changes == settled, "A callback arriving after closing cannot change the draft");
        Check(fixture.State == "Closed", "Closing pins the dialog in its closed state");
        Invoke(fixture.Dialog, "UpdateActions");
        fixture.Dialog.Dispose();
        fixture.Dispose();
    }

    private static void FaultedCleanup()
    {
        var fixture = new Fixture();
        DialogType
            .GetField("_recordWork", Flags)!
            .SetValue(fixture.Dialog, Task.FromException(new InvalidOperationException("后台录音工作失败")));
        fixture.Dialog.Dispose();
        fixture.Pump(() => fixture.CleanupCompleted);
        Check(fixture.CleanupCompleted, "A failed background recording task is observed while closing");
        fixture.Dispose();
    }

    private static Fixture Recording()
    {
        var fixture = new Fixture();
        fixture.Record.PerformClick();
        Check(fixture.Sessions.Count == 1 && fixture.Draft.ReadOnly, "Recording starts exactly one stub session");
        return fixture;
    }

    private static object? Private(object? owner, string name, params object[] args)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        var type = owner?.GetType() ?? DialogType;
        return args.Length > 0
            ? type.GetMethod(name, Any)!.Invoke(owner, args)
            : type.GetField(name, Any)!.GetValue(owner);
    }

    private static void Invoke(object owner, string name, params object[] args) =>
        owner
            .GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(owner, args);

    /// <summary>Synthetic key events, so no real keystroke is ever sent to the desktop.</summary>
    private static void Key(Control control, Keys keys) =>
        typeof(Control).GetMethod("OnKeyDown", Flags)!.Invoke(control, new object[] { new KeyEventArgs(keys) });

    private static T Field<T>(object owner, string name) => (T)Private(owner, name)!;

    /// <summary>One dialog built with a stub session factory, shown off screen so message pumping still works.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly List<StubSession> _sessions = new();

        public Fixture(
            Func<string, CancellationToken, Task>? send = null,
            Func<IDesktopVoiceSession>? factory = null,
            bool show = true
        )
        {
            Dialog = (Form)
                Activator.CreateInstance(
                    DialogType,
                    new object[]
                    {
                        send ?? ((_, _) => Task.CompletedTask),
                        (Func<bool>)(() => CurrentAllowed),
                        factory ?? DefaultFactory,
                    }
                )!;
            Dialog.StartPosition = FormStartPosition.Manual;
            Dialog.Location = new Point(-30000, -30000);
            Dialog.ShowInTaskbar = false;
            Draft = Field<TextBox>(Dialog, "_draft");
            Record = Field<Button>(Dialog, "_record");
            Send = Field<Button>(Dialog, "_send");
            Status = Field<Label>(Dialog, "_status");
            Count = Field<Label>(Dialog, "_count");
            if (show)
            {
                Show();
            }
        }

        public Form Dialog { get; }
        public TextBox Draft { get; }
        public Button Record { get; }
        public Button Send { get; }
        public Label Status { get; }
        public Label Count { get; }
        public bool CurrentAllowed { get; set; } = true;
        public IReadOnlyList<StubSession> Sessions => _sessions;
        public StubSession Last => _sessions[^1];
        public int Generation => Field<int>(Dialog, "_generation");
        public CancellationTokenSource? Life => Field<CancellationTokenSource?>(Dialog, "_recordLife");
        public string State => Private(Dialog, "_state")!.ToString()!;
        public bool CleanupCompleted => Field<Task>(Dialog, "_cleanupWork").IsCompleted;

        public void Show()
        {
            Dialog.Show();
            Application.DoEvents();
            RestoreContext();
        }

        public void Pump(Func<bool> ready)
        {
            PumpUntil(ready);
            RestoreContext();
        }

        /// <summary>The stop button is only offered once the stub session reported that it is listening.</summary>
        public void StopByButton()
        {
            Last.Emit(new("", false, Stage: "recording", Message: "可以说话了"));
            Pump(() => Record.Enabled);
            Record.PerformClick();
            Pump(() => !Draft.ReadOnly);
        }

        public void Dispose()
        {
            if (!Dialog.IsDisposed)
            {
                Dialog.Dispose();
            }
        }

        private IDesktopVoiceSession DefaultFactory()
        {
            var session = new StubSession(null);
            _sessions.Add(session);
            return session;
        }

        private static void RestoreContext()
        {
            if (_ui != null)
            {
                SynchronizationContext.SetSynchronizationContext(_ui);
            }
        }
    }

    /// <summary>A voice session that only raises what the check tells it to; nothing is recorded or recognized.</summary>
    private sealed class StubSession(VoiceRecognitionOutcome? immediate) : IDesktopVoiceSession
    {
        private readonly TaskCompletionSource<VoiceRecognitionOutcome> _done = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public event Action<VoiceRecognitionUpdate>? Update;
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public bool Cancelled { get; private set; }

        public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
        {
            token.Register(() =>
            {
                Cancelled = true;
                _done.TrySetCanceled(token);
            });
            if (immediate != null)
            {
                _done.TrySetResult(immediate);
            }

            return _done.Task;
        }

        public void Stop()
        {
            Stopped = true;
            _done.TrySetResult(new VoiceRecognitionOutcome(false));
        }

        public void Emit(VoiceRecognitionUpdate update) => Update?.Invoke(update);

        public void Finish(VoiceRecognitionOutcome outcome) => _done.TrySetResult(outcome);

        public void Fail(Exception error) => _done.TrySetException(error);

        public void Dispose() => Disposed = true;
    }
}
