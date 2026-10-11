using System.Text;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>On-demand Game Bar recording, using the same local engine as champion select. Never sends chat.</summary>
public sealed class GameBarVoiceRecognition : IDisposable
{
    private sealed class Recording(
        OverlayVoiceRequest request,
        IDesktopVoiceSession recognizer,
        CancellationTokenSource cancellation
    )
    {
        public readonly OverlayVoiceRequest Request = request;
        public readonly IDesktopVoiceSession Recognizer = recognizer;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public long Sequence = 1;
        public string Text = "",
            State = "preparing",
            Hint = "";

        // Complete means worker cleanup finished; Cancelled suppresses publication. Both may be true.
        // State strings remain compatible with the widget protocol.
        public bool Complete,
            Cancelled,
            StopRequested;
        public Task Completion = Task.CompletedTask;
    }

    private readonly object _gate = new();
    private readonly Func<IDesktopVoiceSession> _factory;
    private readonly HashSet<string> _seen = new();
    private readonly SemaphoreSlim _poll = new(1, 1);
    private Recording? _recording;
    private string _session = "",
        _directory = "";
    private long _lastLease;
    private bool _disposed,
        _cleaned,
        _enabled;

    public GameBarVoiceRecognition(Func<IDesktopVoiceSession>? factory = null) =>
        _factory = factory ?? (() => new SenseVoiceSession());

    public async Task PollAsync(
        string directory,
        string session,
        bool enabled,
        CancellationToken token = default,
        long? time = null
    )
    {
        if (_disposed || directory.Length == 0 || !await _poll.WaitAsync(0, token).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            long now = time ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!await PrepareSessionAsync(directory, session, enabled, token).ConfigureAwait(false))
            {
                return;
            }

            var request = await ReadAsync(now).ConfigureAwait(false);
            var recording = _recording;
            if (request == null)
            {
                if (recording != null && now - _lastLease > 3000)
                {
                    Cancel();
                    await DeleteAsync("voice-status.json").ConfigureAwait(false);
                }
                return;
            }
            recording = await AcceptRequestAsync(request, now, token).ConfigureAwait(false);
            if (recording == null)
            {
                return;
            }
            _lastLease = request.UpdatedAt;
            if (request.Action == "cancel")
            {
                Cancel();
                await DeleteAsync("voice-status.json").ConfigureAwait(false);
                return;
            }
            if (request.Action == "stop" && !recording.Complete && !recording.StopRequested)
            {
                lock (_gate)
                {
                    recording.StopRequested = true;
                    recording.State = "stopping";
                    recording.Hint = "";
                    recording.Sequence++;
                }
                recording.Recognizer.Stop();
            }
            await PublishStatusAsync(request, recording, now).ConfigureAwait(false);
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or Newtonsoft.Json.JsonException
                        or FormatException
                        or OverflowException
                        or ArgumentException
            )
        {
            // A partial or concurrently replaced bridge file is retried on the next poll.
        }
        finally
        {
            _poll.Release();
        }
    }

    private async Task<bool> PrepareSessionAsync(
        string directory,
        string session,
        bool enabled,
        CancellationToken token
    )
    {
        if (!_cleaned || _directory != directory)
        {
            _directory = directory;
            _cleaned = true;
            // A new main process must not replay a recording left by a previous instance.
            await DeleteAsync("voice-request.json").ConfigureAwait(false);
            await DeleteAsync("voice-status.json").ConfigureAwait(false);
        }
        if (_session != session || _enabled != enabled || token.IsCancellationRequested)
        {
            Cancel();
            _seen.Clear();
            _session = session;
            _enabled = enabled;
            await DeleteAsync("voice-status.json").ConfigureAwait(false);
            await DeleteAsync("voice-request.json").ConfigureAwait(false);
            return false;
        }
        if (!enabled)
        {
            return false;
        }

        return true;
    }

    private async Task<Recording?> AcceptRequestAsync(OverlayVoiceRequest request, long now, CancellationToken token)
    {
        var recording = _recording;
        if (recording != null && !recording.Request.SameRecording(request))
        {
            Cancel();
            await DeleteAsync("voice-status.json").ConfigureAwait(false);
            // Wait for the previous microphone to be released before accepting a new start.
            if (!recording.Completion.IsCompleted)
            {
                return null;
            }

            recording = null;
        }
        if (recording == null)
        {
            if (
                request.Action != "start"
                || now - request.CreatedAt > 3000
                || _seen.Count >= 256
                || !_seen.Add(request.Id)
            )
            {
                return null;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancellation.CancelAfter(TimeSpan.FromSeconds(55));
            recording = new(request, _factory(), cancellation);
            _recording = recording;
            recording.Completion = RunAsync(recording);
            ChatSendDiagnostics.Shared.Report("voice-recording", "本机中文识别开始。");
        }
        return recording;
    }

    private async Task PublishStatusAsync(OverlayVoiceRequest request, Recording recording, long now)
    {
        JObject? status;
        lock (_gate)
        {
            status =
                recording.Cancelled || request.Ack >= recording.Sequence
                    ? null
                    : new JObject
                    {
                        ["protocol"] = 1,
                        ["id"] = request.Id,
                        ["session"] = request.Session,
                        ["sequence"] = recording.Sequence,
                        ["state"] = recording.State,
                        ["complete"] = recording.Complete,
                        ["text"] = recording.Text,
                        ["hint"] = recording.Hint,
                        ["updatedAt"] = now,
                    };
        }
        // Acknowledged text is removed from transport; the editable draft lives in the widget.
        if (status == null)
        {
            await DeleteAsync("voice-status.json").ConfigureAwait(false);
        }
        else
        {
            await WriteAsync(status).ConfigureAwait(false);
        }
    }

    private void ApplyUpdate(Recording recording, VoiceRecognitionUpdate update)
    {
        lock (_gate)
        {
            if (_disposed || recording.Cancelled || recording.Complete || _recording != recording)
            {
                return;
            }

            if (update.Final)
            {
                string words = Clean(update.Text, 180);
                if (words.Length == 0)
                {
                    return;
                }

                recording.Text = Clean(recording.Text + (recording.Text.Length > 0 ? " " : "") + words, 180);
                recording.Hint = update.Confidence < .4f ? "请检查识别文字。" : "";
                recording.State = "decoding";
                if (recording.Text.Length >= 180)
                {
                    recording.Recognizer.Stop();
                }
            }
            else if (recording.State != "stopping" || update.Stage == "decoding")
            {
                recording.Hint = update.Stage == "recording" ? update.Message : "";
                recording.State = update.Stage;
            }
            recording.Sequence++;
        }
    }

    private async Task RunAsync(Recording recording)
    {
        void Update(VoiceRecognitionUpdate value) => ApplyUpdate(recording, value);
        string failure = "";
        recording.Recognizer.Update += Update;
        try
        {
            var result = await recording.Recognizer.RunAsync(recording.Cancellation.Token).ConfigureAwait(false);
            if (result.Message.Length > 0)
            {
                failure = result.Message;
            }

            if (result.Cancelled && !recording.Cancelled)
            {
                failure = "录音已取消，请重新录音或直接输入。";
            }
        }
        catch (OperationCanceledException)
        {
            failure = "录音已超时，请重新录音或直接输入。";
        }
        catch (VoiceRecognitionException e)
        {
            failure = e.Message;
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("当前没有可用的中文离线语音引擎"))
        {
            failure = e.Message;
        }
        catch (Exception e)
        {
            failure =
                e is UnauthorizedAccessException
                    ? "请在 Windows 设置中允许桌面应用使用麦克风。"
                    : "本机识别失败，请在设置中检查中文模型和默认麦克风；也可以直接输入。";
            ChatSendDiagnostics.Shared.Report("voice-error", $"本机识别失败：{e.GetType().Name} / 0x{e.HResult:x8}");
        }
        finally
        {
            recording.Recognizer.Update -= Update;
            recording.Recognizer.Dispose();
            lock (_gate)
            {
                recording.Complete = true;
                recording.Sequence++;
                recording.State = failure.Length > 0 ? "error" : "done";
                recording.Hint =
                    failure.Length > 0 ? failure
                    : recording.Text.Length == 0 ? "没有识别到文字，请重新录音或直接输入。"
                    : "";
            }
            recording.Cancellation.Dispose();
            ChatSendDiagnostics.Shared.Report("voice-complete", "本机中文识别结束；未发送聊天。");
        }
    }

    // Transport outlives an individual recognizer token, including acknowledgement after cleanup.
    private async Task<OverlayVoiceRequest?> ReadAsync(long now)
    {
        try
        {
            string path = Path.Combine(_directory, "voice-request.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096)
            {
                return null;
            }

            return OverlayVoiceRequest.Parse(
                JObject.Parse(await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(false)),
                _session,
                now
            );
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or Newtonsoft.Json.JsonException
                        or FormatException
                        or OverflowException
                        or ArgumentException
            )
        {
            return null;
        }
    }

    private async Task WriteAsync(JObject status)
    {
        Directory.CreateDirectory(_directory);
        string temporary = Path.Combine(_directory, "voice-status.tmp");
        try
        {
            await File.WriteAllTextAsync(
                    temporary,
                    status.ToString(Newtonsoft.Json.Formatting.None),
                    new UTF8Encoding(false),
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            if (_disposed || _recording?.Cancelled == true)
            {
                return;
            }

            File.Move(temporary, Path.Combine(_directory, "voice-status.json"), true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // Preserve the publication result while cleaning a locked temporary file.
            }
        }
    }

    private Task DeleteAsync(string name)
    {
        try
        {
            File.Delete(Path.Combine(_directory, name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Bridge cleanup is best effort; lease/session checks still prevent replay.
        }
        return Task.CompletedTask;
    }

    private void Cancel()
    {
        lock (_gate)
        {
            if (_recording == null || _recording.Cancelled)
            {
                return;
            }

            _recording.Cancelled = true;
            _recording.Text = "";
            _recording.Hint = "";
            if (!_recording.Complete)
            {
                _recording.Cancellation.Cancel();
                _recording.Recognizer.Dispose();
            }
        }
    }

    private static string Clean(string text, int limit)
    {
        string clean = new(text.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c).ToArray());
        if (clean.Length > limit)
        {
            clean = clean[..limit];
        }

        if (clean.Length > 0 && char.IsHighSurrogate(clean[^1]))
        {
            clean = clean[..^1];
        }

        return clean;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        if (_directory.Length > 0)
        {
            _ = DeleteAsync("voice-status.json");
            _ = DeleteAsync("voice-request.json");
            _ = DeleteAsync("voice-status.tmp");
        }
    }
}
