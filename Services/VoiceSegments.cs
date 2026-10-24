namespace League.Services;

/// <summary>
/// Splits a recording at its pauses, so each phrase reaches SenseVoice on its own. The model reads a whole recording
/// at once and, given several phrases with pauses between them, often returns only one of them; recordings of up to
/// twenty seconds are common. A recording without a clear contrast between speech and background, or with a single
/// phrase, stays whole.
/// </summary>
public static class VoiceSegments
{
    private const int Window = VoicePcmBuffer.SampleRate / 100;

    /// <summary>A pause at least this long separates two phrases; shorter ones are breaths inside a sentence.</summary>
    private const int PauseWindows = 60;

    /// <summary>Lead-in and tail kept around each phrase, so its first and last syllables are not cut.</summary>
    private const int PaddingSamples = VoicePcmBuffer.SampleRate / 5;

    /// <summary>A phrase needs this much speech; shorter bursts are clicks or breaths and are left out.</summary>
    private const int MinimumSpeechWindows = 15;

    /// <summary>Speech must stand this far above the background before the recording is split at all.</summary>
    private const double MinimumContrast = 3;

    /// <summary>The start and length of each phrase, in order; the whole recording when it is not split.</summary>
    public static IReadOnlyList<(int Start, int Length)> Split(float[] samples)
    {
        var whole = new[] { (0, samples.Length) };
        int windows = samples.Length / Window;
        if (windows < PauseWindows * 2)
        {
            return whole;
        }

        var levels = new double[windows];
        for (int w = 0; w < windows; w++)
        {
            double energy = 0;
            for (int i = w * Window; i < (w + 1) * Window; i++)
            {
                energy += samples[i] * samples[i];
            }
            levels[w] = Math.Sqrt(energy / Window);
        }

        var sorted = levels.Order().ToArray();
        double background = sorted[windows / 10];
        double speech = sorted[windows * 95 / 100];
        if (speech <= 0 || speech < background * MinimumContrast)
        {
            return whole;
        }

        double threshold = background + (speech - background) * 0.1;
        var phrases = new List<(int Start, int Length)>();
        int first = -1,
            last = -1,
            spoken = 0;
        void Close()
        {
            if (first >= 0 && spoken >= MinimumSpeechWindows)
            {
                int start = Math.Max(0, first * Window - PaddingSamples);
                int end = Math.Min(samples.Length, (last + 1) * Window + PaddingSamples);
                phrases.Add((start, end - start));
            }
            first = -1;
            spoken = 0;
        }

        for (int w = 0; w < windows; w++)
        {
            if (levels[w] < threshold)
            {
                if (first >= 0 && w - last > PauseWindows)
                {
                    Close();
                }
                continue;
            }

            if (first < 0)
            {
                first = w;
            }
            last = w;
            spoken++;
        }
        Close();

        return phrases.Count > 1 ? phrases : whole;
    }

    /// <summary>
    /// Joins the phrases' text: a comma where the model left no punctuation of its own, so the line still reads as
    /// one message.
    /// </summary>
    public static string Join(IEnumerable<string> phrases)
    {
        var text = new System.Text.StringBuilder();
        foreach (string phrase in phrases.Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (text.Length > 0 && !"，。！？、；,.!?;".Contains(text[^1]))
            {
                text.Append('，');
            }
            text.Append(phrase);
        }
        return text.ToString();
    }
}
