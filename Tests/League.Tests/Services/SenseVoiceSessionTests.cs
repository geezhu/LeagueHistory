using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The recording session's decoding step. The capture returns prepared audio and the transcriber records what it is
/// given, so no microphone or model is touched: a recording with a long pause reaches the model phrase by phrase, and
/// the phrases come back as one line.
/// </summary>
public class SenseVoiceSessionTests
{
    private const int Rate = VoicePcmBuffer.SampleRate;

    private sealed class PreparedCapture(float[] audio) : IVoiceAudioCapture
    {
        public Task<float[]> RecordAsync(Action<VoiceRecognitionUpdate> update, CancellationToken token) =>
            Task.FromResult(audio);

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class RecordingTranscriber(params string[] answers) : IVoiceTranscriber
    {
        public List<int> Lengths { get; } = new();

        public Task PrepareAsync(CancellationToken token) => Task.CompletedTask;

        public Task<string> TranscribeAsync(float[] samples, CancellationToken token)
        {
            Lengths.Add(samples.Length);
            return Task.FromResult(answers[Math.Min(Lengths.Count - 1, answers.Length - 1)]);
        }

        public void Dispose() { }
    }

    private static float[] Tone(double seconds, float amplitude) =>
        [
            .. Enumerable
                .Range(0, (int)(seconds * Rate))
                .Select(i => amplitude * MathF.Sin(i * 2 * MathF.PI * 220 / Rate)),
        ];

    private static async Task<string> RecognizeAsync(float[] audio, RecordingTranscriber transcriber)
    {
        using var session = new SenseVoiceSession(new PreparedCapture(audio), transcriber);
        string text = "";
        session.Update += update =>
        {
            if (update.Final)
            {
                text = update.Text;
            }
        };
        await session.RunAsync(CancellationToken.None);
        return text;
    }

    [Fact]
    public async Task RunAsync_ARecordingWithALongPause_IsDecodedPhraseByPhrase()
    {
        var transcriber = new RecordingTranscriber("先别开团", "等队友到齐");
        float[] audio = [.. Tone(2, .05f), .. Tone(1.5, .0005f), .. Tone(2, .05f)];

        await RecognizeAsync(audio, transcriber);

        Assert.Equal(2, transcriber.Lengths.Count);
    }

    [Fact]
    public async Task RunAsync_PhrasesDecodedApart_ComeBackAsOneLine()
    {
        var transcriber = new RecordingTranscriber("先别开团", "等队友到齐");
        float[] audio = [.. Tone(2, .05f), .. Tone(1.5, .0005f), .. Tone(2, .05f)];

        Assert.Equal("先别开团，等队友到齐", await RecognizeAsync(audio, transcriber));
    }

    [Fact]
    public async Task RunAsync_OneContinuousPhrase_IsDecodedWhole()
    {
        var transcriber = new RecordingTranscriber("推中路");
        float[] audio = Tone(3, .05f);

        await RecognizeAsync(audio, transcriber);

        Assert.Equal([audio.Length], transcriber.Lengths);
    }
}
