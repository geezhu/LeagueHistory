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
    private int _stop,
        _started;
    private readonly Action<SpeechRecognitionEngine> _input;

    public DesktopVoiceSession(Action<SpeechRecognitionEngine>? input = null) =>
        _input = input ?? (engine => engine.SetInputToDefaultAudioDevice());

    public event Action<VoiceRecognitionUpdate>? Update;

    public void Stop() => Interlocked.CompareExchange(ref _stop, 1, 0);

    public void Dispose() => Interlocked.Exchange(ref _stop, 2);

    public static SpeechRecognitionEngine CreateChineseEngine()
    {
        var info = SpeechRecognitionEngine
            .InstalledRecognizers()
            .FirstOrDefault(r =>
                r.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || r.Culture.Name.Equals("zh-Hans-CN", StringComparison.OrdinalIgnoreCase)
            );
        if (info == null)
            throw new InvalidOperationException(
                "当前没有可用的中文离线语音引擎，请检查 Windows 中文语音组件；也可以直接输入文字。"
            );
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
            throw new InvalidOperationException("录音会话已经使用。");
        return Task.Run(
            async () =>
            {
                token.ThrowIfCancellationRequested();
                using var engine = CreateChineseEngine();
                if (token.IsCancellationRequested || Volatile.Read(ref _stop) != 0)
                    return new VoiceRecognitionOutcome(true);
                var completed = new TaskCompletionSource<VoiceRecognitionOutcome>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                void Recognized(object? sender, SpeechRecognizedEventArgs args) =>
                    Emit(new(args.Result.Text, true, args.Result.Confidence));
                void Hypothesis(object? sender, SpeechHypothesizedEventArgs args) => Emit(new(args.Result.Text, false));
                void Finished(object? sender, RecognizeCompletedEventArgs args)
                {
                    if (args.Error != null)
                        completed.TrySetException(args.Error);
                    else
                        completed.TrySetResult(new(args.Cancelled));
                }
                engine.SpeechRecognized += Recognized;
                engine.SpeechHypothesized += Hypothesis;
                engine.RecognizeCompleted += Finished;
                try
                {
                    _input(engine);
                    if (token.IsCancellationRequested || Volatile.Read(ref _stop) != 0)
                        return new VoiceRecognitionOutcome(true);
                    engine.RecognizeAsync(RecognizeMode.Multiple);
                    Emit(new("", false));
                    var watch = Stopwatch.StartNew();
                    bool stopping = false;
                    while (!completed.Task.IsCompleted)
                    {
                        int stop = Volatile.Read(ref _stop);
                        if (token.IsCancellationRequested || stop == 2)
                        {
                            engine.RecognizeAsyncCancel();
                            stopping = true;
                            break;
                        }
                        if (stop == 1 || watch.Elapsed >= TimeSpan.FromSeconds(20))
                        {
                            engine.RecognizeAsyncStop();
                            stopping = true;
                            break;
                        }
                        await Task.WhenAny(completed.Task, Task.Delay(100)).ConfigureAwait(false);
                    }
                    if (
                        stopping
                        && await Task.WhenAny(completed.Task, Task.Delay(3000)).ConfigureAwait(false) != completed.Task
                    )
                    {
                        engine.RecognizeAsyncCancel();
                        return new(
                            token.IsCancellationRequested || Volatile.Read(ref _stop) == 2,
                            "录音已停止，请检查已有文字后再发送。"
                        );
                    }
                    return await completed.Task.ConfigureAwait(false);
                }
                finally
                {
                    engine.SpeechRecognized -= Recognized;
                    engine.SpeechHypothesized -= Hypothesis;
                    engine.RecognizeCompleted -= Finished;
                }
                void Emit(VoiceRecognitionUpdate value)
                {
                    if (!token.IsCancellationRequested && Volatile.Read(ref _stop) != 2)
                        Update?.Invoke(value);
                }
            },
            token
        );
    }
}
