using SherpaOnnx;

namespace League.Services;

public sealed class SenseVoiceTranscriber(VoiceModelStore? modelStore = null) : IVoiceTranscriber
{
    private OfflineRecognizer? _recognizer;

    public Task PrepareAsync(CancellationToken token) =>
        Task.Run(
            async () =>
            {
                if (!Environment.Is64BitProcess)
                {
                    throw new VoiceRecognitionException("SenseVoice 需要 64 位程序，请使用 Windows x64 版本。");
                }
                string directory = await (modelStore ?? VoiceModelStore.Shared)
                    .ValidateAsync(token)
                    .ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var config = new OfflineRecognizerConfig();
                config.FeatConfig.SampleRate = VoicePcmBuffer.SampleRate;
                config.FeatConfig.FeatureDim = 80;
                config.ModelConfig.Tokens = Path.Combine(directory, "tokens.txt");
                config.ModelConfig.SenseVoice.Model = Path.Combine(directory, "model.int8.onnx");
                config.ModelConfig.SenseVoice.Language = "zh";
                config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
                config.ModelConfig.NumThreads = 2;
                config.ModelConfig.Provider = "cpu";
                config.ModelConfig.Debug = 0;
                config.DecodingMethod = "greedy_search";
                _recognizer = new OfflineRecognizer(config);
                token.ThrowIfCancellationRequested();
            },
            token
        );

    public Task<string> TranscribeAsync(float[] samples, CancellationToken token) =>
        Task.Run(
            () =>
            {
                token.ThrowIfCancellationRequested();
                var recognizer = _recognizer ?? throw new VoiceRecognitionException("中文模型尚未准备完成。");
                using var stream = recognizer.CreateStream();
                stream.AcceptWaveform(VoicePcmBuffer.SampleRate, samples);
                recognizer.Decode([stream]);
                token.ThrowIfCancellationRequested();
                return stream.Result.Text.Trim();
            },
            token
        );

    public void Dispose()
    {
        _recognizer?.Dispose();
        _recognizer = null;
    }
}
