using System.Diagnostics;
using System.Speech.Recognition;

namespace League.Services;

public sealed record VoiceRecognitionUpdate(
    string Text,
    bool Final,
    float Confidence = 1,
    string Stage = "recording",
    string Message = ""
);

public sealed record VoiceRecognitionOutcome(bool Cancelled, string Message = "");

public interface IDesktopVoiceSession : IDisposable
{
    event Action<VoiceRecognitionUpdate>? Update;
    Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token);
    void Stop();
}

/// <summary>One on-demand local Chinese dictation session. Engine work and disposal stay off the UI thread.</summary>
public sealed class DesktopVoiceSession : IDesktopVoiceSession
{
    private enum StopState
    {
        Active,
        StopRequested,
        Disposed,
    }

    private StopState CurrentState => (StopState)Volatile.Read(ref _stop);

    private int _stop,
        _started;
    private readonly Action<SpeechRecognitionEngine> _input;

    public DesktopVoiceSession(Action<SpeechRecognitionEngine>? input = null) =>
        _input = input ?? (engine => engine.SetInputToDefaultAudioDevice());

    public event Action<VoiceRecognitionUpdate>? Update;

    public void Stop() => Interlocked.CompareExchange(ref _stop, (int)StopState.StopRequested, (int)StopState.Active);

    public void Dispose() => Interlocked.Exchange(ref _stop, (int)StopState.Disposed);

    public static SpeechRecognitionEngine CreateChineseEngine()
    {
        var info = SpeechRecognitionEngine
            .InstalledRecognizers()
            .FirstOrDefault(r =>
                r.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || r.Culture.Name.Equals("zh-Hans-CN", StringComparison.OrdinalIgnoreCase)
            );
        if (info == null)
        {
            throw new InvalidOperationException(
                "当前没有可用的中文离线语音引擎，请检查 Windows 中文语音组件；也可以直接输入文字。"
            );
        }

        var engine = new SpeechRecognitionEngine(info);
        try
        {
            engine.LoadGrammar(new DictationGrammar());
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(4);
            engine.BabbleTimeout = TimeSpan.FromSeconds(4);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(700);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(900);
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("录音会话已经使用。");
        }
        return Task.Run(() => RunSessionAsync(token), token);
    }

    private async Task<VoiceRecognitionOutcome> RunSessionAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var engine = CreateChineseEngine();
        if (token.IsCancellationRequested || CurrentState != StopState.Active)
        {
            return new VoiceRecognitionOutcome(true);
        }

        var completed = new TaskCompletionSource<VoiceRecognitionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        void Recognized(object? sender, SpeechRecognizedEventArgs args) =>
            Emit(new(args.Result.Text, true, args.Result.Confidence));
        void Hypothesis(object? sender, SpeechHypothesizedEventArgs args) => Emit(new(args.Result.Text, false));
        void Finished(object? sender, RecognizeCompletedEventArgs args)
        {
            if (args.Error != null)
            {
                completed.TrySetException(args.Error);
            }
            else
            {
                completed.TrySetResult(new(args.Cancelled));
            }
        }
        engine.SpeechRecognized += Recognized;
        engine.SpeechHypothesized += Hypothesis;
        engine.RecognizeCompleted += Finished;
        try
        {
            _input(engine);
            if (token.IsCancellationRequested || CurrentState != StopState.Active)
            {
                return new VoiceRecognitionOutcome(true);
            }

            engine.RecognizeAsync(RecognizeMode.Multiple);
            Emit(new("", false));
            return await WaitForCompletionAsync(engine, completed.Task, token).ConfigureAwait(false);
        }
        finally
        {
            engine.SpeechRecognized -= Recognized;
            engine.SpeechHypothesized -= Hypothesis;
            engine.RecognizeCompleted -= Finished;
        }
        void Emit(VoiceRecognitionUpdate value)
        {
            if (!token.IsCancellationRequested && CurrentState != StopState.Disposed)
            {
                Update?.Invoke(value);
            }
        }
    }

    private async Task<VoiceRecognitionOutcome> WaitForCompletionAsync(
        SpeechRecognitionEngine engine,
        Task<VoiceRecognitionOutcome> completion,
        CancellationToken token
    )
    {
        // Observe cancellation in the loop so the native engine is stopped before disposal.
        // The polling and final grace period must themselves survive caller cancellation.
        var watch = Stopwatch.StartNew();
        bool stopping = false;
        while (!completion.IsCompleted)
        {
            var stop = CurrentState;
            if (token.IsCancellationRequested || stop == StopState.Disposed)
            {
                engine.RecognizeAsyncCancel();
                stopping = true;
                break;
            }
            if (stop == StopState.StopRequested || watch.Elapsed >= TimeSpan.FromSeconds(20))
            {
                engine.RecognizeAsyncStop();
                stopping = true;
                break;
            }
            await Task.WhenAny(completion, Task.Delay(100, CancellationToken.None)).ConfigureAwait(false);
        }
        if (
            stopping
            && await Task.WhenAny(completion, Task.Delay(3000, CancellationToken.None)).ConfigureAwait(false)
                != completion
        )
        {
            engine.RecognizeAsyncCancel();
            return new(
                token.IsCancellationRequested || CurrentState == StopState.Disposed,
                "录音已停止，请检查已有文字后再发送。"
            );
        }
        return await completion.ConfigureAwait(false);
    }
}
