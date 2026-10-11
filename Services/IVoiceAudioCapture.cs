namespace League.Services;

public interface IVoiceAudioCapture : IDisposable
{
    Task<float[]> RecordAsync(Action<VoiceRecognitionUpdate> update, CancellationToken token);
    void Stop();
}
