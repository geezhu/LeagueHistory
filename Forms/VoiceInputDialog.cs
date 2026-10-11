using System.Collections.Concurrent;
using League.Models;
using League.Services;

namespace League;

internal sealed class VoiceInputDialog : Form
{
    private readonly TextBox _draft = new()
    {
        Name = "voiceDraft",
        Multiline = true,
        WordWrap = true,
        MaxLength = 180,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "录音后检查文字，或直接输入",
    };
    private readonly Label _status = new() { Name = "voiceStatus", Dock = DockStyle.Fill };
    private readonly Label _count = new()
    {
        AutoSize = true,
        Text = "0 / 180",
        Dock = DockStyle.Right,
    };
    private readonly Button _record = ActionButton("开始录音", "voiceRecord", 94),
        _send = ActionButton("确认发送", "voiceSend", 90),
        _clear = ActionButton("清空", "voiceClear", 56),
        _close = ActionButton("关闭", "voiceClose", 56);
    private readonly Func<string, CancellationToken, Task> _sendMessage;
    private readonly Func<bool> _current;
    private readonly Func<IDesktopVoiceSession> _factory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Font _dialogFont = new("Microsoft YaHei UI", 9);
    private readonly Font _draftFont = new("Microsoft YaHei UI", 10);
    private Task _recordWork = Task.CompletedTask;
    private Task _sendWork = Task.CompletedTask;
    private Task _cleanupWork = Task.CompletedTask;
    private CancellationTokenSource? _recordLife;
    private IDesktopVoiceSession? _session;

    private enum DialogState
    {
        Idle,
        Preparing,
        Recording,
        Stopping,
        Decoding,
        Sending,
        Closed,
    }

    private DialogState _state;
    private bool IsRecording =>
        _state is DialogState.Preparing or DialogState.Recording or DialogState.Stopping or DialogState.Decoding;
    private bool IsSending => _state == DialogState.Sending;

    private int _generation;

    public VoiceInputDialog(
        Func<string, CancellationToken, Task> sendMessage,
        Func<bool> current,
        Func<IDesktopVoiceSession>? factory = null
    )
    {
        _sendMessage = sendMessage;
        _current = current;
        _factory = factory ?? (() => new SenseVoiceSession());
        ConfigureSurface();
        BindActions();
        UpdateActions();
    }

    private void ConfigureSurface()
    {
        Text = "选人语音输入";
        Name = "VoiceInputDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.FromArgb(215, 225, 226);
        Font = _dialogFont;
        ClientSize = new Size(380, 234);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4,
        };
        foreach (int height in new[] { 26, 0, 48, 38 })
        {
            layout.RowStyles.Add(
                new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height)
            );
        }

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label { Text = "聊天文字", AutoSize = true });
        heading.Controls.Add(_count);
        layout.Controls.Add(heading, 0, 0);
        _draft.BackColor = Color.FromArgb(12, 39, 51);
        _draft.ForeColor = ForeColor;
        _draft.BorderStyle = BorderStyle.FixedSingle;
        _draft.Font = _draftFont;
        ConfigureDraftInput();
        layout.Controls.Add(_draft, 0, 1);
        _status.Padding = new Padding(0, 6, 0, 0);
        _status.ForeColor = Color.FromArgb(145, 172, 187);
        layout.Controls.Add(_status, 0, 2);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
        };
        actions.Controls.AddRange(new Control[] { _record, _send, _clear, _close });
        _send.BackColor = Color.FromArgb(0, 126, 139);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
    }

    private void ConfigureDraftInput()
    {
        _draft.TextChanged += (_, _) =>
        {
            UpdateActions();
            if (!IsRecording && !IsSending)
            {
                _status.Text =
                    _draft.TextLength == 0 || OverlayVoiceChatText.TryNormalize(_draft.Text, out _)
                        ? ""
                        : "请使用单行聊天文字，最多 180 字符；不支持斜杠命令。";
            }
        };
        _draft.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
            }
        };
    }

    private void BindActions()
    {
        _record.Click += async (_, _) =>
        {
            if (IsRecording)
            {
                StopRecording();
            }
            else
            {
                _recordWork = RecordAsync();
                await _recordWork;
            }
        };
        _send.Click += async (_, _) =>
        {
            _sendWork = SendAsync();
            await _sendWork;
        };
        _clear.Click += (_, _) => _draft.Clear();
        _close.Click += (_, _) => Close();
        Shown += (_, _) => UpdateActions();
        UpdateActions();
    }

    private static Button ActionButton(string text, string name, int width)
    {
        var button = new Button
        {
            Text = text,
            Name = name,
            Size = new Size(width, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = Color.FromArgb(220, 230, 226),
            Margin = new Padding(0, 0, 5, 0),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(49, 83, 95);
        return button;
    }

    private void UpdateActions()
    {
        if (IsDisposed || _state == DialogState.Closed)
        {
            return;
        }
        bool current = !_lifetime.IsCancellationRequested && _current();
        _draft.ReadOnly = IsRecording || IsSending;
        _record.Text = _state switch
        {
            DialogState.Decoding => "识别中…",
            DialogState.Stopping => "停止中…",
            DialogState.Recording => "停止录音",
            DialogState.Preparing => "准备中…",
            _ => _draft.TextLength == 0 ? "开始录音" : "重新录音",
        };
        _record.Enabled = current && (_state is DialogState.Idle or DialogState.Recording);
        _send.Enabled = current && _state == DialogState.Idle && OverlayVoiceChatText.TryNormalize(_draft.Text, out _);
        _clear.Enabled = _state == DialogState.Idle && _draft.TextLength > 0;
        _count.Text = $"{_draft.TextLength} / 180";
    }

    private void StopRecording()
    {
        if (!IsRecording)
        {
            return;
        }
        _state = DialogState.Stopping;
        _session?.Stop();
        UpdateActions();
    }

    private bool RecordingCurrent(int generation, CancellationTokenSource source) =>
        !IsDisposed
        && _state != DialogState.Closed
        && generation == _generation
        && !source.IsCancellationRequested
        && _current();

    private async Task RecordAsync()
    {
        if (_state != DialogState.Idle || !_current())
        {
            return;
        }
        int generation = ++_generation;
        _state = DialogState.Preparing;
        _status.Text = "";
        UpdateActions();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _recordLife = source;
        IDesktopVoiceSession? session = null;
        var pending = new ConcurrentQueue<VoiceRecognitionUpdate>();
        void Drain() => DrainUpdates(generation, source, pending);
        void Receive(VoiceRecognitionUpdate update) => QueueUpdate(update, pending, Drain);
        try
        {
            session = _factory();
            _session = session;
            session.Update += Receive;
            _draft.Clear();
            var outcome = await session.RunAsync(source.Token);
            // Process terminal callbacks before unlocking the editable draft.
            Drain();
            if (RecordingCurrent(generation, source))
            {
                _status.Text =
                    outcome.Message.Length > 0 ? outcome.Message
                    : outcome.Cancelled ? "录音已取消。"
                    : _draft.TextLength == 0 ? "没有识别到文字，请重录或直接输入。"
                    : "";
            }
        }
        catch (OperationCanceledException)
        {
            if (RecordingCurrent(generation, source))
            {
                _status.Text = "录音已取消。";
            }
        }
        catch (Exception error)
        {
            ReportError(error);
            if (RecordingCurrent(generation, source))
            {
                _status.Text = DescribeVoiceError(error);
            }
        }
        finally
        {
            if (session != null)
            {
                session.Update -= Receive;
                session.Dispose();
            }
            pending.Clear();
            _session = null;
            _recordLife = null;
            if (_state != DialogState.Closed)
            {
                _state = DialogState.Idle;
                UpdateActions();
            }
        }
    }

    private void QueueUpdate(
        VoiceRecognitionUpdate update,
        ConcurrentQueue<VoiceRecognitionUpdate> pending,
        Action drain
    )
    {
        if (IsDisposed || !IsHandleCreated || _state == DialogState.Closed)
        {
            return;
        }
        pending.Enqueue(update);
        try
        {
            BeginInvoke(drain);
        }
        catch (InvalidOperationException)
        {
            // Closing the dialog can destroy its handle between the check and the queued callback.
            pending.Clear();
        }
    }

    private void DrainUpdates(
        int generation,
        CancellationTokenSource source,
        ConcurrentQueue<VoiceRecognitionUpdate> pending
    )
    {
        if (!RecordingCurrent(generation, source) || !IsRecording)
        {
            pending.Clear();
            return;
        }
        while (pending.TryDequeue(out var update))
        {
            if (!update.Final)
            {
                if (update.Stage == "decoding")
                {
                    _state = DialogState.Decoding;
                }
                else if (update.Stage == "recording" && _state != DialogState.Stopping)
                {
                    _state = DialogState.Recording;
                }
                _status.Text = _state == DialogState.Recording ? update.Message : "";
                UpdateActions();
                continue;
            }
            string chunk = new(
                update
                    .Text.Select(character =>
                        char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character
                    )
                    .ToArray()
            );
            _draft.Text = Clip((_draft.TextLength > 0 ? _draft.Text + " " : "") + chunk, 180);
            _status.Text = update.Confidence < .3f ? "请检查识别文字。" : "";
            if (_draft.TextLength >= 180)
            {
                StopRecording();
            }
        }
    }

    private static string DescribeVoiceError(Exception error) =>
        error switch
        {
            VoiceRecognitionException => error.Message,
            InvalidOperationException when error.Message.StartsWith("当前没有可用") => error.Message,
            InvalidOperationException => "中文语音引擎或麦克风不可用，请检查语音组件和麦克风权限；也可直接输入。",
            _ => "麦克风或本机中文模型不可用，请在设置中检查模型和麦克风权限，或直接输入。",
        };

    private static void ReportError(Exception error) =>
        ChatSendDiagnostics.Shared.Report(
            "voice-error",
            $"选人录音失败：{error.GetType().Name} / 0x{error.HResult:X8}。"
        );

    private static string Clip(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        int count = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..count];
    }

    private async Task SendAsync()
    {
        if (_state != DialogState.Idle || !_current())
        {
            return;
        }

        if (!OverlayVoiceChatText.TryNormalize(_draft.Text, out string text))
        {
            _status.Text = "请输入一条不超过 180 字符的聊天文字；不支持换行或斜杠命令。";
            return;
        }
        _state = DialogState.Sending;
        _status.Text = "正在发送到选人聊天…";
        UpdateActions();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await _sendMessage(text, timeout.Token);
            if (!IsDisposed)
            {
                _draft.Clear();
                _status.Text = "已发送到选人聊天。";
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                _status.Text = "发送取消或超时，消息可能已经送达，请先检查选人聊天。";
            }
        }
        catch (InvalidOperationException exception)
        {
            if (!IsDisposed)
            {
                _status.Text = exception.Message;
            }
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                _status.Text = "发送失败，请检查客户端聊天连接。";
            }
        }
        finally
        {
            if (_state != DialogState.Closed)
            {
                _state = DialogState.Idle;
            }
            if (!IsDisposed)
            {
                UpdateActions();
            }
        }
    }

    private async Task FinishClosedWorkAsync(Task cancel)
    {
        try
        {
            await cancel.ConfigureAwait(false);
            await Task.WhenAll(_recordWork, _sendWork).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // A cleanup failure must be observed without reviving the closed dialog or replacing its primary error.
            System.Diagnostics.Debug.WriteLine($"Voice dialog closed-work cleanup: {error}");
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        bool releaseFonts = disposing && _state != DialogState.Closed;
        if (releaseFonts)
        {
            _state = DialogState.Closed;
            ++_generation;
            var cancellation = _lifetime.CancelAsync();
            _session?.Dispose();
            _cleanupWork = FinishClosedWorkAsync(cancellation);
        }
        base.Dispose(disposing);
        if (releaseFonts)
        {
            _dialogFont.Dispose();
            _draftFont.Dispose();
        }
    }
}
