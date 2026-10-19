using System.Buffers.Binary;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The microphone sample buffer behind voice input: it counts 16 kHz samples, decodes them from raw little-endian PCM
/// bytes, reports activity, keeps the speech part and applies a bounded gain. No device is opened here; every case
/// hands the buffer a hand-built byte array. The expected values come from the checks in
/// Tests/StatisticsChecks/VoicePcmBufferChecks.cs and the capture checks in SenseVoiceChecks.cs.
/// </summary>
public class VoicePcmBufferTests
{
    /// <summary>How many samples one analysis window holds; the activity level only moves when a window closes.</summary>
    private const int Window = VoicePcmBuffer.SampleRate / 100;

    [Fact]
    public void SampleRate_Default_IsSixteenThousand()
    {
        Assert.Equal(16000, VoicePcmBuffer.SampleRate);
    }

    [Fact]
    public void MaxSamples_Default_IsTwentySecondsOfMono()
    {
        Assert.Equal(320000, VoicePcmBuffer.MaxSamples);
    }

    [Theory]
    [InlineData(new short[] { 3 }, new byte[] { 3, 0 })]
    [InlineData(new short[] { -1 }, new byte[] { 255, 255 })]
    [InlineData(new short[] { -32768 }, new byte[] { 0, 128 })]
    [InlineData(new short[] { 32767, 0 }, new byte[] { 255, 127, 0, 0 })]
    public void Pcm_IntendedSamples_EncodesThemAsLittleEndianBytes(short[] samples, byte[] expected)
    {
        byte[] pcm = Pcm(samples);

        Assert.Equal(expected, pcm);
    }

    [Theory]
    [InlineData(new byte[] { 3, 0 }, 3f / 32768f)]
    [InlineData(new byte[] { 0, 3 }, 768f / 32768f)]
    [InlineData(new byte[] { 255, 255 }, 1f / 32768f)]
    [InlineData(new byte[] { 0, 128 }, 1f)]
    public void Append_TwoBytes_ReadsOneSignedLittleEndianSample(byte[] pcm, float expectedPeak)
    {
        Assert.Equal(2, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(1, buffer.Count);
        Assert.Equal(expectedPeak, buffer.Peak);
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 1f / 32768f)]
    [InlineData(100, 100f / 32768f)]
    [InlineData(-100, 100f / 32768f)]
    [InlineData(32767, 32767f / 32768f)]
    [InlineData(-32768, 1f)]
    public void Peak_OneSample_IsItsAmplitudeRegardlessOfSign(short sample, float expectedPeak)
    {
        byte[] pcm = Pcm(sample);
        Assert.Equal(2, pcm.Length);
        Assert.Equal(sample, BinaryPrimitives.ReadInt16LittleEndian(pcm));

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(expectedPeak, buffer.Peak);
    }

    [Fact]
    public void Count_ThreeSamples_KeepsEveryOne()
    {
        byte[] pcm = Pcm(100, -100, 32767);
        Assert.Equal(6, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Count_OddByteCount_DropsTheTrailingHalfSample()
    {
        byte[] pcm = [1, 2, 3];
        Assert.Equal(3, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(1, buffer.Count);
        Assert.Equal(513f / 32768f, buffer.Peak);
    }

    [Fact]
    public void Count_HalfSampleFollowedByAnotherPacket_DoesNotCarryTheLeftoverByte()
    {
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5];
        Assert.Equal(3, first.Length);
        Assert.Equal(2, second.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(first);
        buffer.Append(second);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(1284f / 32768f, buffer.Peak);
    }

    [Fact]
    public void Append_NoBytes_LeavesTheBufferEmpty()
    {
        var buffer = new VoicePcmBuffer();

        buffer.Append([]);

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0f, buffer.Peak);
        Assert.Equal(0, buffer.Level);
        Assert.Empty(buffer.Finish());
    }

    [Fact]
    public void Append_NoBytesAfterSpeech_KeepsWhatWasAlreadyHeard()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(Window, 16384));
        Assert.Equal(92, buffer.Level);

        buffer.Append([]);

        Assert.Equal(Window, buffer.Count);
        Assert.Equal(92, buffer.Level);
    }

    [Theory]
    [InlineData(VoicePcmBuffer.MaxSamples - 1, VoicePcmBuffer.MaxSamples - 1)]
    [InlineData(VoicePcmBuffer.MaxSamples, VoicePcmBuffer.MaxSamples)]
    [InlineData(VoicePcmBuffer.MaxSamples + 1, VoicePcmBuffer.MaxSamples)]
    public void Count_AppendAroundTheTwentySecondCap_StopsAtMaxSamples(int sampleCount, int expectedCount)
    {
        byte[] pcm = Tone(sampleCount, 16384);
        Assert.Equal(sampleCount * 2, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(expectedCount, buffer.Count);
    }

    [Fact]
    public void Count_FullWindowAfterTheCap_IgnoresTheNewSamplesInsteadOfReplacingTheOldOnes()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(VoicePcmBuffer.MaxSamples, 0));
        Assert.Equal(VoicePcmBuffer.MaxSamples, buffer.Count);

        buffer.Append(Tone(Window, 32767));

        Assert.Equal(VoicePcmBuffer.MaxSamples, buffer.Count);
        Assert.Equal(0f, buffer.Peak);
        Assert.Equal(0, buffer.Level);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(328, 50)]
    [InlineData(16384, 92)]
    [InlineData(-32768, 100)]
    public void Level_OneFullWindow_ReportsTheActivityOfThatWindow(short amplitude, int expectedLevel)
    {
        byte[] pcm = Tone(Window, amplitude);
        Assert.Equal(Window * 2, pcm.Length);
        Assert.Equal(amplitude, BinaryPrimitives.ReadInt16LittleEndian(pcm));

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(expectedLevel, buffer.Level);
    }

    [Fact]
    public void Level_BeforeTheFirstWindowCloses_StaysAtZeroUntilTheLastSampleArrives()
    {
        byte[] pending = Tone(Window - 1, 16384);
        Assert.Equal((Window - 1) * 2, pending.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pending);

        Assert.Equal(0, buffer.Level);

        buffer.Append(Tone(1, 16384));

        Assert.Equal(92, buffer.Level);
    }

    [Fact]
    public void Level_LoudWindowFollowedBySilence_ReportsTheLatestWindowWhilePeakKeepsTheLoudest()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(Window, 16384));
        Assert.Equal(92, buffer.Level);

        buffer.Append(Tone(Window, 0));

        Assert.Equal(0, buffer.Level);
        Assert.Equal(0.5f, buffer.Peak);
    }

    [Fact]
    public void Level_WindowHoldingOneTinyClick_ReportsNoActivityAlthoughSomethingWasHeard()
    {
        byte[] pcm = Click(Window, 1);
        Assert.Equal(Window * 2, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(0, buffer.Level);
        Assert.Equal(1f / 32768f, buffer.Peak);
    }

    [Fact]
    public void Level_WindowHoldingOneFullScaleClick_ReportsTheWindowAverageNotThePeak()
    {
        byte[] pcm = Click(Window, -32768);
        Assert.Equal(Window * 2, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(72, buffer.Level);
        Assert.Equal(1f, buffer.Peak);
    }

    [Fact]
    public void Finish_RecordingThatNeverGotASample_KeepsNothing()
    {
        var buffer = new VoicePcmBuffer();

        Assert.Empty(buffer.Finish());
    }

    [Fact]
    public void Finish_OneSecondOfDigitalSilence_KeepsNothing()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(VoicePcmBuffer.SampleRate, 0));

        Assert.Empty(buffer.Finish());
    }

    [Theory]
    [InlineData(Window * 9, 0)]
    [InlineData(Window * 10, Window * 10)]
    public void Finish_AudibleSpanAtTheTenthSecondGate_IsDroppedOrKept(int sampleCount, int expectedLength)
    {
        byte[] pcm = Tone(sampleCount, 16384);
        Assert.Equal(sampleCount * 2, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(expectedLength, buffer.Finish().Length);
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, Window * 10)]
    public void Finish_WindowAtTheQuietGates_IsKeptOnlyOnceBothThresholdsAreCleared(short amplitude, int expectedLength)
    {
        byte[] pcm = Tone(Window * 10, amplitude);
        Assert.Equal(Window * 20, pcm.Length);
        Assert.Equal(amplitude, BinaryPrimitives.ReadInt16LittleEndian(pcm));

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        Assert.Equal(expectedLength, buffer.Finish().Length);
    }

    [Fact]
    public void Finish_HalfASecondOfSilenceBeforeSpeech_KeepsAQuarterSecondOfLeadIn()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(VoicePcmBuffer.SampleRate / 2, 0));
        buffer.Append(Tone(VoicePcmBuffer.SampleRate / 2, 16384));

        Assert.Equal(VoicePcmBuffer.SampleRate * 3 / 4, buffer.Finish().Length);
    }

    [Fact]
    public void Finish_SpeechFollowedByASecondOfSilence_KeepsAThirdOfASecondOfContext()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(VoicePcmBuffer.SampleRate / 2, 16384));
        buffer.Append(Tone(VoicePcmBuffer.SampleRate, 0));

        Assert.Equal(VoicePcmBuffer.SampleRate / 2 + VoicePcmBuffer.SampleRate / 3, buffer.Finish().Length);
    }

    [Theory]
    [InlineData(16384, 0.5f)]
    [InlineData(33, 0.01611328125f)]
    [InlineData(7, 0.00341796875f)]
    public void Finish_QuietRecording_IsAmplifiedByAtMostSixteenTimes(short amplitude, float expectedPeak)
    {
        byte[] pcm = Tone(VoicePcmBuffer.SampleRate / 2, amplitude);
        Assert.Equal(VoicePcmBuffer.SampleRate, pcm.Length);

        var buffer = new VoicePcmBuffer();
        buffer.Append(pcm);

        float[] kept = buffer.Finish();

        Assert.Equal(VoicePcmBuffer.SampleRate / 2, kept.Length);
        Assert.Equal(expectedPeak, kept.Max(Math.Abs));
    }

    [Fact]
    public void Finish_PartialWindow_ClosesItAndReportsItsActivity()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(Window / 2, 16384));
        Assert.Equal(0, buffer.Level);

        _ = buffer.Finish();

        Assert.Equal(92, buffer.Level);
    }

    [Fact]
    public void Finish_CalledTwice_ReturnsTheSameAudioAndKeepsTheSamples()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(VoicePcmBuffer.SampleRate / 2, 16384));

        float[] first = buffer.Finish();
        float[] second = buffer.Finish();

        Assert.Equal(first, second);
        Assert.Equal(VoicePcmBuffer.SampleRate / 2, buffer.Count);
    }

    [Fact]
    public void Finish_SameAudioAppendedInChunks_MatchesTheSingleWholePacket()
    {
        byte[] pcm = Tone(VoicePcmBuffer.SampleRate, 16000);
        Assert.Equal(VoicePcmBuffer.SampleRate * 2, pcm.Length);

        var whole = new VoicePcmBuffer();
        whole.Append(pcm);
        var chunked = new VoicePcmBuffer();
        AppendInChunks(chunked, pcm, 402);

        Assert.Equal(whole.Count, chunked.Count);
        Assert.Equal(whole.Finish(), chunked.Finish());
    }

    [Fact]
    public void Clear_RecordingWithSpeech_ErasesSamplesActivityAndVoiceBounds()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(VoicePcmBuffer.SampleRate, 16384));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0f, buffer.Peak);
        Assert.Equal(0, buffer.Level);
        Assert.Empty(buffer.Finish());
    }

    [Fact]
    public void Clear_BetweenTwoRecordings_StartsTheSecondOneFromScratch()
    {
        var buffer = new VoicePcmBuffer();
        buffer.Append(Tone(Window, 16384));
        buffer.Clear();
        byte[] pcm = Tone(Window, 4);
        Assert.Equal(Window * 2, pcm.Length);

        buffer.Append(pcm);

        Assert.Equal(Window, buffer.Count);
        Assert.Equal(4f / 32768f, buffer.Peak);
        Assert.Equal(2, buffer.Level);
    }

    /// <summary>Encodes samples the way the capture hands them to the buffer: 16-bit little-endian PCM bytes.</summary>
    private static byte[] Pcm(params short[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), samples[i]);
        }

        return pcm;
    }

    /// <summary>A block of <paramref name="count"/> equally loud samples.</summary>
    private static byte[] Tone(int count, short amplitude) => Pcm(Enumerable.Repeat(amplitude, count).ToArray());

    /// <summary>A window whose first sample is the click and whose remaining samples are digital silence.</summary>
    private static byte[] Click(int count, short amplitude)
    {
        var samples = new short[count];
        samples[0] = amplitude;
        return Pcm(samples);
    }

    /// <summary>Splits the same bytes into packets, so window boundaries fall inside a packet.</summary>
    private static void AppendInChunks(VoicePcmBuffer buffer, byte[] pcm, int chunkBytes)
    {
        for (int offset = 0; offset < pcm.Length; offset += chunkBytes)
        {
            buffer.Append(pcm.AsSpan(offset, Math.Min(chunkBytes, pcm.Length - offset)));
        }
    }
}
