using System.Buffers.Binary;
using League.Services;
using static StatisticsCheckSupport;

/// <summary>
/// The microphone buffer behind voice input: it holds 16 kHz samples, decides which part carried sound and applies
/// a bounded gain. No device is opened here; the checks hand it sample arrays.
/// </summary>
internal static class VoicePcmBufferChecks
{
    public static void Run()
    {
        Counting();
        ActivityLevel();
        KeepingOnlySpeech();
        GainAndLimits();
        Console.WriteLine(
            "PASS: microphone sample counting, activity level, speech trimming, bounded gain, duration cap and clearing."
        );
    }

    private static void Counting()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Pcm(100, -100, 32767));
        Check(buffer.Count == 3, "Every 16-bit sample is kept");
        Check(Math.Abs(buffer.Peak - 32767f / 32768f) < 0.0001, "The peak is the loudest sample heard so far");

        var odd = new VoicePcmBuffer();
        odd.Append([1, 2, 3]);
        Check(odd.Count == 1, "A trailing half sample is ignored");

        Check(new VoicePcmBuffer().Finish().Length == 0, "A recording that never got a sample keeps nothing");
    }

    private static void ActivityLevel()
    {
        var silence = new VoicePcmBuffer();
        silence.Append(Pcm(new short[VoicePcmBuffer.SampleRate / 100]));
        Check(silence.Level == 0, "Digital silence reports no activity");
        Check(silence.Finish().Length == 0, "A recording of silence keeps nothing");

        var loud = new VoicePcmBuffer();
        loud.Append(Pcm(Tones(VoicePcmBuffer.SampleRate / 100, 16384)));
        Check(loud.Level > 80, "A loud window reports high activity");

        var faint = new VoicePcmBuffer();
        faint.Append(Pcm(Tones(VoicePcmBuffer.SampleRate / 100, 328)));
        Check(faint.Level is > 40 and < 60, "A faint window reports a middling activity level");
    }

    private static void KeepingOnlySpeech()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Pcm(new short[VoicePcmBuffer.SampleRate / 2]));
        buffer.Append(Pcm(Tones(VoicePcmBuffer.SampleRate / 2, 16384)));
        var kept = buffer.Finish();
        Check(
            kept.Length == VoicePcmBuffer.SampleRate * 3 / 4,
            "The kept audio is the speech plus a quarter second of lead-in"
        );
        Check(Math.Abs(kept.Max(Math.Abs) - 0.5f) < 0.001, "Audio below the gain threshold keeps its own level");

        var brief = new VoicePcmBuffer();
        brief.Append(Pcm(new short[VoicePcmBuffer.SampleRate]));
        brief.Append(Pcm(Tones(VoicePcmBuffer.SampleRate / 20, 16384)));
        Check(brief.Finish().Length == 0, "Speech shorter than a tenth of a second is dropped");
    }

    private static void GainAndLimits()
    {
        var quiet = new VoicePcmBuffer();
        quiet.Append(Pcm(Tones(VoicePcmBuffer.SampleRate / 2, 33)));
        var amplified = quiet.Finish();
        Check(amplified.Length > 0, "Quiet speech is still kept");
        Check(
            Math.Abs(amplified.Max(Math.Abs) - 33f / 32768f * 16) < 0.001,
            "A quiet recording is amplified by at most sixteen times"
        );

        var capped = new VoicePcmBuffer();
        capped.Append(Pcm(Tones(VoicePcmBuffer.MaxSamples + VoicePcmBuffer.SampleRate, 16384)));
        Check(capped.Count == VoicePcmBuffer.MaxSamples, "The buffer stops taking samples after twenty seconds");

        var cleared = new VoicePcmBuffer();
        cleared.Append(Pcm(Tones(VoicePcmBuffer.SampleRate, 16384)));
        cleared.Clear();
        Check(
            cleared.Count == 0 && cleared.Peak == 0 && cleared.Level == 0 && cleared.Finish().Length == 0,
            "Clearing drops the samples and everything measured from them"
        );
    }

    private static short[] Tones(int count, short amplitude) => Enumerable.Repeat(amplitude, count).ToArray();

    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }
        return bytes;
    }
}
