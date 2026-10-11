namespace League.Services;

public interface IVoiceTranscriber : IDisposable
{
    Task PrepareAsync(CancellationToken token);
    Task<string> TranscribeAsync(float[] samples, CancellationToken token);
}
