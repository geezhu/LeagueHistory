using System.Diagnostics;
using League.Managers;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace League.Services;

/// <summary>Owns microphone readiness and recording; native resources stay on its worker.</summary>
public sealed class MicrophoneVoiceCapture : IVoiceAudioCapture
{
    private readonly Func<IWaveIn>? _inputFactory;
    private int _state;

    public MicrophoneVoiceCapture(Func<IWaveIn>? inputFactory = null)
    {
        _inputFactory = inputFactory;
    }

    private VoiceCaptureState State => (VoiceCaptureState)Volatile.Read(ref _state);

    public void Stop()
    {
        Interlocked.CompareExchange(ref _state, (int)VoiceCaptureState.StopRequested, (int)VoiceCaptureState.Running);
    }

    public void Dispose()
    {
        // Flag cancellation here; only the recording worker disposes the native input.
        Interlocked.Exchange(ref _state, (int)VoiceCaptureState.Disposed);
    }

    public Task<float[]> RecordAsync(Action<VoiceRecognitionUpdate> update, CancellationToken token)
    {
        return Task.Run(() => RecordWorkerAsync(update, token), token);
    }

    private async Task<float[]> RecordWorkerAsync(Action<VoiceRecognitionUpdate> update, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (State != VoiceCaptureState.Running)
        {
            return [];
        }

        using var device =
            _inputFactory == null
                ? VoiceMicrophoneDevices.Open(LeagueConfigManager.Load().VoiceMicrophoneId ?? "")
                : null;
        string name = device?.FriendlyName ?? "测试麦克风";
        if (
            device != null
            && (device.AudioEndpointVolume.Mute || device.AudioEndpointVolume.MasterVolumeLevelScalar <= 0)
        )
        {
            throw new VoiceRecognitionException("所选麦克风已静音或音量为零，请在 Windows 声音设置中调整后重录。");
        }

        // WASAPI shared mode converts native stereo/48k float audio to mono PCM16.
        using var input =
            _inputFactory?.Invoke()
            ?? new WasapiCapture(device!, false, 50) { WaveFormat = new WaveFormat(VoicePcmBuffer.SampleRate, 16, 1) };
        var recording = new RecordingEvents(() => State != VoiceCaptureState.Disposed);
        input.DataAvailable += recording.OnDataAvailable;
        input.RecordingStopped += recording.OnRecordingStopped;

        try
        {
            input.StartRecording();
            ChatSendDiagnostics.Shared.Report("voice-capture-start", "WASAPI 共享采集 · 16000 Hz / PCM16 / 单声道。");
            var watch = Stopwatch.StartNew();
            await WaitForReadinessAsync(recording.Ready.Task, recording.Ended.Task, watch, token).ConfigureAwait(false);
            if (
                recording.Ready.Task.IsCompleted
                && !token.IsCancellationRequested
                && State == VoiceCaptureState.Running
            )
            {
                update(new("", false, Stage: "recording", Message: $"可以说话了 · {name}"));
            }

            await WaitForStopAsync(recording.Ended.Task, watch, token).ConfigureAwait(false);
            input.StopRecording();
            // Cancellation must not bypass native shutdown; report it only after the device stops.
            await recording.Ended.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (State == VoiceCaptureState.Disposed)
            {
                return [];
            }

            return recording.Finish();
        }
        finally
        {
            Interlocked.Exchange(ref _state, (int)VoiceCaptureState.Disposed);
            recording.Clear();
        }
    }

    private async Task WaitForReadinessAsync(Task ready, Task ended, Stopwatch watch, CancellationToken token)
    {
        while (
            !ready.IsCompleted
            && !ended.IsCompleted
            && !token.IsCancellationRequested
            && State == VoiceCaptureState.Running
            && watch.Elapsed < TimeSpan.FromSeconds(5)
        )
        {
            // Poll cancellation so the caller can stop the native input before propagating cancellation.
            await Task.WhenAny(ready, ended, Task.Delay(50, CancellationToken.None)).ConfigureAwait(false);
        }

        if (
            !ready.IsCompleted
            && !ended.IsCompleted
            && !token.IsCancellationRequested
            && State == VoiceCaptureState.Running
        )
        {
            throw new VoiceRecognitionException("麦克风启动超时，请检查设备和权限后重新录音。");
        }
    }

    private async Task WaitForStopAsync(Task ended, Stopwatch watch, CancellationToken token)
    {
        while (
            !ended.IsCompleted
            && !token.IsCancellationRequested
            && State == VoiceCaptureState.Running
            && watch.Elapsed < TimeSpan.FromSeconds(20)
        )
        {
            await Task.WhenAny(ended, Task.Delay(50, CancellationToken.None)).ConfigureAwait(false);
        }
    }

    // Callback synchronization and readiness are grouped together; they never load a model or touch the UI.
    private sealed class RecordingEvents(Func<bool> acceptData)
    {
        private readonly object _gate = new();
        private readonly VoicePcmBuffer _samples = new();
        private int _packets;

        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            lock (_gate)
            {
                if (acceptData())
                {
                    _samples.Append(args.Buffer.AsSpan(0, args.BytesRecorded));
                }
            }

            if (args.BytesRecorded > 0)
            {
                Interlocked.Increment(ref _packets);
                Ready.TrySetResult();
            }
        }

        public void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception != null)
            {
                Ended.TrySetException(args.Exception);
            }
            else
            {
                Ended.TrySetResult();
            }
        }

        public float[] Finish()
        {
            lock (_gate)
            {
                float[] result = _samples.Finish();
                ChatSendDiagnostics.Shared.Report(
                    "voice-capture-end",
                    $"数据包 {_packets} · 采样 {_samples.Count} · 峰值 {_samples.Peak:F6} · 有效采样 {result.Length}。"
                );
                if (_packets == 0)
                {
                    throw new VoiceRecognitionException(
                        "麦克风没有返回录音数据，请到设置 → 消息发送重新选择设备，并检查 Windows 麦克风权限。"
                    );
                }

                return result;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _samples.Clear();
            }
        }
    }
}
