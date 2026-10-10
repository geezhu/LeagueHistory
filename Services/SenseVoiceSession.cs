using NAudio.Wave;
using NAudio.CoreAudioApi;
using SherpaOnnx;
using System.Diagnostics;
using League.Managers;

namespace League.Services;

public interface IVoiceAudioCapture : IDisposable
{
    Task<float[]> RecordAsync(Action<VoiceRecognitionUpdate> update,CancellationToken token);
    void Stop();
}
public interface IVoiceTranscriber : IDisposable
{
    Task PrepareAsync(CancellationToken token);
    Task<string> TranscribeAsync(float[] samples,CancellationToken token);
}

/// <summary>One bounded recording followed by offline Chinese recognition. Only confirmed text can reach chat.</summary>
public sealed class SenseVoiceSession : IDesktopVoiceSession
{
    private static readonly SemaphoreSlim SingleRecording=new(1,1);
    private readonly IVoiceAudioCapture _capture;
    private readonly IVoiceTranscriber _transcriber;
    private readonly CancellationTokenSource _life=new();
    private int _started,_stop;
    public SenseVoiceSession(IVoiceAudioCapture? capture=null,IVoiceTranscriber? transcriber=null)
    {_capture=capture??new MicrophoneVoiceCapture();_transcriber=transcriber??new SenseVoiceTranscriber();}
    public event Action<VoiceRecognitionUpdate>? Update;
    public void Stop(){Interlocked.CompareExchange(ref _stop,1,0);_capture.Stop();}
    public void Dispose(){if(Interlocked.Exchange(ref _stop,2)==2)return;try{_life.Cancel();}catch(ObjectDisposedException){ }_capture.Dispose();}
    public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
    {
        if(Interlocked.Exchange(ref _started,1)!=0)throw new VoiceRecognitionException("录音会话已经使用。");
        return Task.Run<VoiceRecognitionOutcome>(async()=>
        {
            using var source=CancellationTokenSource.CreateLinkedTokenSource(token,_life.Token);
            bool held=false;
            Task? preparation=null;Task<float[]>? recording=null;float[]? samples=null;
            try
            {
                if(Volatile.Read(ref _stop)!=0)return new(true);
                if(!await SingleRecording.WaitAsync(0,source.Token).ConfigureAwait(false))throw new VoiceRecognitionException("上一段语音仍在处理，请稍后重新录音。");
                held=true;Emit(new("",false,Stage:"preparing"));
                // Keep microphone startup independent of model loading; audio stays bounded in memory.
                preparation=_transcriber.PrepareAsync(source.Token);
                recording=_capture.RecordAsync(Emit,source.Token);
                var first=await Task.WhenAny(preparation,recording).ConfigureAwait(false);
                if(first==preparation)await preparation.ConfigureAwait(false); // Fail fast and cancel capture if model loading fails.
                samples=await recording.ConfigureAwait(false);
                source.Token.ThrowIfCancellationRequested();if(Volatile.Read(ref _stop)==2)return new(true);
                if(samples.Length<1600)return new(false,"未收到有效声音，请到设置 → 消息发送选择麦克风，检查静音和输入音量后重录。");
                Emit(new("",false,Stage:"decoding"));
                await preparation.ConfigureAwait(false);source.Token.ThrowIfCancellationRequested();
                string text=await _transcriber.TranscribeAsync(samples,source.Token).ConfigureAwait(false);
                source.Token.ThrowIfCancellationRequested();if(Volatile.Read(ref _stop)==2)return new(true);
                if(!string.IsNullOrWhiteSpace(text))Emit(new(text,true,float.NaN,Stage:"done"));
                return new(false);
            }
            catch(OperationCanceledException)when(source.IsCancellationRequested){return new(true);}
            finally
            {
                Interlocked.Exchange(ref _stop,2);source.Cancel();_capture.Dispose();
                // Both workers must finish before disposing a model or releasing the recording gate.
                if(recording!=null)try{var captured=await recording.ConfigureAwait(false);if(!ReferenceEquals(captured,samples))Array.Clear(captured);}catch{ }
                if(preparation!=null)try{await preparation.ConfigureAwait(false);}catch{ }
                if(samples!=null)Array.Clear(samples);
                _transcriber.Dispose();_life.Dispose();if(held)SingleRecording.Release();
            }
            void Emit(VoiceRecognitionUpdate update){if(!source.IsCancellationRequested && Volatile.Read(ref _stop)!=2)Update?.Invoke(update);}
        },CancellationToken.None);
    }
}

public sealed class SenseVoiceTranscriber(VoiceModelStore? modelStore=null) : IVoiceTranscriber
{
    private OfflineRecognizer? _recognizer;
    public Task PrepareAsync(CancellationToken token)=>Task.Run(async()=>
    {
        if(!Environment.Is64BitProcess)throw new VoiceRecognitionException("SenseVoice 需要 64 位程序，请使用 Windows x64 版本。");
        string directory=await (modelStore??VoiceModelStore.Shared).ValidateAsync(token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
        var config=new OfflineRecognizerConfig();config.FeatConfig.SampleRate=16000;config.FeatConfig.FeatureDim=80;
        config.ModelConfig.Tokens=Path.Combine(directory,"tokens.txt");config.ModelConfig.SenseVoice.Model=Path.Combine(directory,"model.int8.onnx");
        config.ModelConfig.SenseVoice.Language="zh";config.ModelConfig.SenseVoice.UseInverseTextNormalization=1;
        config.ModelConfig.NumThreads=2;config.ModelConfig.Provider="cpu";config.ModelConfig.Debug=0;
        config.DecodingMethod="greedy_search";_recognizer=new OfflineRecognizer(config);token.ThrowIfCancellationRequested();
    },token);
    public Task<string> TranscribeAsync(float[] samples,CancellationToken token)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();var recognizer=_recognizer??throw new VoiceRecognitionException("中文模型尚未准备完成。");
        using var stream=recognizer.CreateStream();stream.AcceptWaveform(16000,samples);recognizer.Decode([stream]);
        token.ThrowIfCancellationRequested();return stream.Result.Text.Trim();
    },token);
    public void Dispose(){_recognizer?.Dispose();_recognizer=null;}
}

public sealed class VoicePcmBuffer
{
    public const int SampleRate=16000,MaxSamples=SampleRate*20;
    private readonly List<float> _samples=new(MaxSamples);
    private int _firstVoice=-1,_lastVoice;
    private double _energy;
    private int _windowSamples,_audibleSamples;
    private float _peak,_windowPeak;
    public int Count=>_samples.Count;
    public float Peak=>_peak;
    public int Level {get;private set;}
    public void Append(ReadOnlySpan<byte> pcm)
    {
        int count=Math.Min(pcm.Length/2,MaxSamples-_samples.Count);
        for(int i=0;i<count;i++)
        {
            float value=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i*2,2))/32768f;
            _samples.Add(value);_energy+=value*value;_windowSamples++;
            float absolute=Math.Abs(value);_peak=Math.Max(_peak,absolute);_windowPeak=Math.Max(_windowPeak,absolute);
            if(_windowSamples==SampleRate/100)AnalyzeWindow();
        }
    }
    private void AnalyzeWindow()
    {
        if(_windowSamples==0)return;
        double rms=Math.Sqrt(_energy/_windowSamples);
        // Exclude near-digital silence, not quiet speech. The old .004 threshold discarded entire recordings.
        if(rms>=.0001 && _windowPeak>=.0002)
        {
            if(_firstVoice<0)_firstVoice=_samples.Count-_windowSamples;
            _lastVoice=_samples.Count;_audibleSamples+=_windowSamples;
        }
        // A logarithmic activity indicator; this is not a speech-confidence score.
        Level=rms<=.00001?0:Math.Clamp((int)Math.Round((20*Math.Log10(rms)+80)/.8),0,100);
        _energy=0;_windowSamples=0;_windowPeak=0;
    }
    public float[] Finish()
    {
        AnalyzeWindow();
        if(_firstVoice<0 || _audibleSamples<SampleRate/10)return [];
        int first=Math.Max(0,_firstVoice-SampleRate/4),last=Math.Min(_samples.Count,_lastVoice+SampleRate/3);
        var result=_samples.GetRange(first,last-first).ToArray();
        // Bounded gain makes quiet microphones usable without changing the system's input volume.
        float gain=_peak>0?Math.Min(16,.2f/_peak):1;
        if(gain>1)for(int i=0;i<result.Length;i++)result[i]*=gain;
        return result;
    }
    public void Clear(){System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_samples).Clear();_samples.Clear();_firstVoice=-1;_lastVoice=_windowSamples=_audibleSamples=Level=0;_energy=0;_peak=_windowPeak=0;}
}

public sealed class MicrophoneVoiceCapture : IVoiceAudioCapture
{
    private int _stop;
    private readonly Func<IWaveIn>? _inputFactory;
    public MicrophoneVoiceCapture(Func<IWaveIn>? inputFactory=null)=>_inputFactory=inputFactory;
    public void Stop()=>Interlocked.CompareExchange(ref _stop,1,0);
    // Native audio disposal is performed on the recording worker, never on a UI event.
    public void Dispose()=>Interlocked.Exchange(ref _stop,2);
    public Task<float[]> RecordAsync(Action<VoiceRecognitionUpdate> update,CancellationToken token)=>Task.Run(async()=>
    {
        token.ThrowIfCancellationRequested();if(Volatile.Read(ref _stop)!=0)return [];
        var gate=new object();var samples=new VoicePcmBuffer();
        using var device=_inputFactory==null?VoiceMicrophoneDevices.Open(LeagueConfigManager.Load().VoiceMicrophoneId??""):null;
        string name=device?.FriendlyName??"测试麦克风";
        if(device!=null && (device.AudioEndpointVolume.Mute || device.AudioEndpointVolume.MasterVolumeLevelScalar<=0))
            throw new VoiceRecognitionException("所选麦克风已静音或音量为零，请在 Windows 声音设置中调整后重录。");
        // WASAPI shared mode converts the device's native format (often stereo 48k float) to mono PCM16.
        using var input=_inputFactory?.Invoke()??new WasapiCapture(device!,false,50){WaveFormat=new WaveFormat(VoicePcmBuffer.SampleRate,16,1)};
        var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int packets=0;
        input.DataAvailable+=(_,args)=>{lock(gate)if(Volatile.Read(ref _stop)!=2)samples.Append(args.Buffer.AsSpan(0,args.BytesRecorded));};
        input.DataAvailable+=(_,args)=>{if(args.BytesRecorded>0){Interlocked.Increment(ref packets);ready.TrySetResult();}};
        input.RecordingStopped+=(_,args)=>{if(args.Exception!=null)ended.TrySetException(args.Exception);else ended.TrySetResult();};
        try
        {
            input.StartRecording();
            ChatSendDiagnostics.Shared.Report("voice-capture-start","WASAPI 共享采集 · 16000 Hz / PCM16 / 单声道。");
            var watch=Stopwatch.StartNew();
            while(!ready.Task.IsCompleted && !ended.Task.IsCompleted && !token.IsCancellationRequested && Volatile.Read(ref _stop)==0 && watch.Elapsed<TimeSpan.FromSeconds(5))
                await Task.WhenAny(ready.Task,ended.Task,Task.Delay(50)).ConfigureAwait(false);
            if(!ready.Task.IsCompleted && !ended.Task.IsCompleted && !token.IsCancellationRequested && Volatile.Read(ref _stop)==0)
                throw new VoiceRecognitionException("麦克风启动超时，请检查设备和权限后重新录音。");
            if(ready.Task.IsCompleted && !token.IsCancellationRequested && Volatile.Read(ref _stop)==0)
                update(new("",false,Stage:"recording",Message:$"可以说话了 · {name}"));
            while(!ended.Task.IsCompleted && !token.IsCancellationRequested && Volatile.Read(ref _stop)==0 && watch.Elapsed<TimeSpan.FromSeconds(20))
            {
                await Task.WhenAny(ended.Task,Task.Delay(50)).ConfigureAwait(false);
            }
            input.StopRecording();await ended.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();if(Volatile.Read(ref _stop)==2)return [];
            lock(gate)
            {
                float[] result=samples.Finish();
                ChatSendDiagnostics.Shared.Report("voice-capture-end",$"数据包 {packets} · 采样 {samples.Count} · 峰值 {samples.Peak:F6} · 有效采样 {result.Length}。");
                if(packets==0)throw new VoiceRecognitionException("麦克风没有返回录音数据，请到设置 → 消息发送重新选择设备，并检查 Windows 麦克风权限。");
                return result;
            }
        }
        finally{Interlocked.Exchange(ref _stop,2);lock(gate)samples.Clear();}
    },token);
}
