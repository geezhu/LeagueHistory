using System.Collections.Concurrent;
using League.Models;
using League.Services;

namespace League;

internal sealed class VoiceInputDialog : Form
{
    // Multiline keeps pasted line breaks visible to validation; one row without wrapping keeps it a chat line.
    private readonly TextBox _draft = new()
    {
        Name = "voiceDraft",
        Multiline = true,
        WordWrap = false,
        MaxLength = 180,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.None,
        PlaceholderText = "说话或直接输入，Enter 发送",
    };
    private readonly Label _status = new()
    {
        Name = "voiceStatus",
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
    };
    private readonly Label _count = new()
    {
        AutoSize = true,
        Text = "0 / 180",
        Dock = DockStyle.Right,
    };

    // One row: a microphone toggle, the chat line and send. Clearing is plain editing; closing is the title bar or Esc.
    private readonly Button _record = ActionButton("", "voiceRecord", 34),
        _send = ActionButton("发送", "voiceSend", 60);
    private readonly ToolTip _tips = new();
    private bool _recordWhenShown;
    private readonly Func<string, CancellationToken, Task> _sendMessage;
    private readonly Func<bool> _current;
    private readonly Func<IDesktopVoiceSession> _factory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Font _dialogFont = new("Microsoft YaHei UI", 9);
    private readonly Font _draftFont = new("Microsoft YaHei UI", 10);
    private readonly Font _iconFont = new("Segoe MDL2 Assets", 11);
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
        Text = "语音输入 · 选人聊天";
        Name = "VoiceInputDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.FromArgb(215, 225, 226);
        Font = _dialogFont;
        ClientSize = new Size(380, 76);
        KeyPreview = true;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 10, 10, 4),
            ColumnCount = 3,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _record.Font = _iconFont;
        _record.Margin = new Padding(0, 2, 6, 2);
        _draft.BackColor = Color.FromArgb(12, 39, 51);
        _draft.ForeColor = ForeColor;
        _draft.BorderStyle = BorderStyle.FixedSingle;
        _draft.Font = _draftFont;
        _draft.Margin = new Padding(0, 2, 0, 2);
        ConfigureDraftInput();
        _send.Margin = new Padding(6, 2, 0, 2);
        _send.BackColor = League.Controls.AccentColors.Fill;
        layout.Controls.Add(_record, 0, 0);
        layout.Controls.Add(_draft, 1, 0);
        layout.Controls.Add(_send, 2, 0);
        // Status and length share one small line under the chat text.
        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _status.ForeColor = _count.ForeColor = Color.FromArgb(145, 172, 187);
        footer.Controls.Add(_status);
        footer.Controls.Add(_count);
        layout.Controls.Add(footer, 1, 1);
        layout.SetColumnSpan(footer, 2);
        Controls.Add(layout);
    }

    /// <summary>
    /// Starts recording once the dialog is shown, for the assistant's microphone button. Checks and previews
    /// construct the dialog without it, so showing a dialog alone never opens a microphone.
    /// </summary>
    public void StartRecordingWhenShown() => _recordWhenShown = true;

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
                // Enter is the explicit confirmation; it never inserts a line break.
                e.SuppressKeyPress = true;
                if (_send.Enabled)
                {
                    _send.PerformClick();
                }
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
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
        Shown += (_, _) =>
        {
            UpdateActions();
            if (_recordWhenShown && _record.Enabled && !IsRecording)
            {
                _record.PerformClick();
            }
        };
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
        string recordState = _state switch
        {
            DialogState.Decoding => "识别中…",
            DialogState.Stopping => "停止中…",
            DialogState.Recording => "停止录音",
            DialogState.Preparing => "准备中…",
            _ => _draft.TextLength == 0 ? "开始录音" : "重新录音",
        };
        // Microphone, stop square, or an ellipsis while the recognizer prepares or decodes.
        _record.Text =
            _state == DialogState.Recording ? "\uE71A"
            : IsRecording ? "\uE712"
            : "\uE720";
        _record.AccessibleName = recordState;
        _record.BackColor = _state == DialogState.Recording ? Color.FromArgb(120, 44, 44) : Color.FromArgb(16, 45, 57);
        if (_tips.GetToolTip(_record) != recordState)
        {
            _tips.SetToolTip(_record, recordState);
        }
        _record.Enabled = current && (_state is DialogState.Idle or DialogState.Recording);
        _send.Enabled = current && _state == DialogState.Idle && OverlayVoiceChatText.TryNormalize(_draft.Text, out _);
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
            AppLog.Shared.Warn(LogCategory.Ui, "Voice dialog closed-work cleanup", error);
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
            _iconFont.Dispose();
            _tips.Dispose();
        }
    }
}
