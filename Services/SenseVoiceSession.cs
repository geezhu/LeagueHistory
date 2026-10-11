namespace League.Services;

/// <summary>Coordinates one bounded recording and offline recognition; it never sends chat.</summary>
public sealed class SenseVoiceSession : IDesktopVoiceSession
{
    private static readonly SemaphoreSlim SingleRecording = new(1, 1);
    private readonly IVoiceAudioCapture _capture;
    private readonly IVoiceTranscriber _transcriber;
    private readonly CancellationTokenSource _lifetime = new();
    private int _started;
    private int _state;

    public SenseVoiceSession(IVoiceAudioCapture? capture = null, IVoiceTranscriber? transcriber = null)
    {
        _capture = capture ?? new MicrophoneVoiceCapture();
        _transcriber = transcriber ?? new SenseVoiceTranscriber();
    }

    public event Action<VoiceRecognitionUpdate>? Update;

    private VoiceCaptureState State => (VoiceCaptureState)Volatile.Read(ref _state);

    public void Stop()
    {
        Interlocked.CompareExchange(ref _state, (int)VoiceCaptureState.StopRequested, (int)VoiceCaptureState.Running);
        _capture.Stop();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _state, (int)VoiceCaptureState.Disposed) == (int)VoiceCaptureState.Disposed)
        {
            return;
        }

        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The recording worker may have completed and disposed this source concurrently.
        }

        _capture.Dispose();
    }

    public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new VoiceRecognitionException("录音会话已经使用。");
        }

        // The worker handles cancellation and must run its cleanup even for a cancelled token.
        return Task.Run(() => RunWorkerAsync(token), CancellationToken.None);
    }

    private async Task<VoiceRecognitionOutcome> RunWorkerAsync(CancellationToken token)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        bool gateHeld = false;
        Task? preparation = null;
        Task<float[]>? recording = null;
        float[]? samples = null;

        try
        {
            if (State != VoiceCaptureState.Running)
            {
                return new(true);
            }

            if (!await SingleRecording.WaitAsync(0, source.Token).ConfigureAwait(false))
            {
                throw new VoiceRecognitionException("上一段语音仍在处理，请稍后重新录音。");
            }

            gateHeld = true;
            Emit(source, new("", false, Stage: "preparing"));

            // Microphone startup and model preparation are independent; audio stays bounded in memory.
            preparation = _transcriber.PrepareAsync(source.Token);
            recording = _capture.RecordAsync(update => Emit(source, update), source.Token);
            var first = await Task.WhenAny(preparation, recording).ConfigureAwait(false);
            if (first == preparation)
            {
                // Surface a model load failure immediately so cleanup can cancel capture.
                await preparation.ConfigureAwait(false);
            }

            samples = await recording.ConfigureAwait(false);
            source.Token.ThrowIfCancellationRequested();
            if (State == VoiceCaptureState.Disposed)
            {
                return new(true);
            }

            return await RecognizeAsync(source, preparation, samples).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            return new(true);
        }
        finally
        {
            await CleanupAsync(source, recording, preparation, samples, gateHeld).ConfigureAwait(false);
        }
    }

    private async Task<VoiceRecognitionOutcome> RecognizeAsync(
        CancellationTokenSource source,
        Task preparation,
        float[] samples
    )
    {
        if (samples.Length < VoicePcmBuffer.SampleRate / 10)
        {
            return new(false, "未收到有效声音，请到设置 → 消息发送选择麦克风，检查静音和输入音量后重录。");
        }

        Emit(source, new("", false, Stage: "decoding"));
        await preparation.ConfigureAwait(false);
        source.Token.ThrowIfCancellationRequested();
        string text = await _transcriber.TranscribeAsync(samples, source.Token).ConfigureAwait(false);
        source.Token.ThrowIfCancellationRequested();
        if (State == VoiceCaptureState.Disposed)
        {
            return new(true);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            Emit(source, new(text, true, float.NaN, Stage: "done"));
        }

        return new(false);
    }

    private async Task CleanupAsync(
        CancellationTokenSource source,
        Task<float[]>? recording,
        Task? preparation,
        float[]? samples,
        bool gateHeld
    )
    {
        Interlocked.Exchange(ref _state, (int)VoiceCaptureState.Disposed);
        await source.CancelAsync().ConfigureAwait(false);
        _capture.Dispose();

        // Join both workers before disposing the model or allowing another recording.
        if (recording != null)
        {
            try
            {
                var captured = await recording.ConfigureAwait(false);
                if (!ReferenceEquals(captured, samples))
                {
                    Array.Clear(captured);
                }
            }
            catch
            {
                // The primary operation reports worker errors; cleanup also tolerates cancellation.
            }
        }

        if (preparation != null)
        {
            try
            {
                await preparation.ConfigureAwait(false);
            }
            catch
            {
                // Observe a late model failure without replacing the primary error/cancellation.
            }
        }

        if (samples != null)
        {
            Array.Clear(samples);
        }

        _transcriber.Dispose();
        _lifetime.Dispose();
        if (gateHeld)
        {
            SingleRecording.Release();
        }
    }

    private void Emit(CancellationTokenSource source, VoiceRecognitionUpdate update)
    {
        if (!source.IsCancellationRequested && State != VoiceCaptureState.Disposed)
        {
            Update?.Invoke(update);
        }
    }
}
