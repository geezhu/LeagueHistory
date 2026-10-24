using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Splitting a recording at its pauses before recognition. The audio is built from tones (speech) and a faint hum
/// (background), so the split points are known: a pause of at least 0.6 seconds separates phrases, shorter ones do
/// not, and each phrase keeps 0.2 seconds on either side.
/// </summary>
public class VoiceSegmentsTests
{
    private const int Rate = VoicePcmBuffer.SampleRate;

    private static float[] Tone(double seconds, float amplitude) =>
        [
            .. Enumerable
                .Range(0, (int)(seconds * Rate))
                .Select(i => amplitude * MathF.Sin(i * 2 * MathF.PI * 220 / Rate)),
        ];

    private static float[] Audio(params float[][] parts) => [.. parts.SelectMany(part => part)];

    private static float[] Speech(double seconds) => Tone(seconds, .05f);

    private static float[] Pause(double seconds) => Tone(seconds, .0005f);

    [Fact]
    public void Split_OneContinuousPhrase_KeepsTheWholeRecording()
    {
        float[] audio = Audio(Pause(.5), Speech(3), Pause(.5));

        Assert.Equal([(0, audio.Length)], VoiceSegments.Split(audio));
    }

    [Fact]
    public void Split_TwoPhrasesAroundALongPause_GivesTwoPhrases()
    {
        float[] audio = Audio(Speech(2), Pause(1.5), Speech(2));

        Assert.Equal(2, VoiceSegments.Split(audio).Count);
    }

    [Fact]
    public void Split_AShortBreathInsideASentence_DoesNotSplit()
    {
        float[] audio = Audio(Speech(2), Pause(.3), Speech(2));

        Assert.Single(VoiceSegments.Split(audio));
    }

    [Fact]
    public void Split_EachPhrase_KeepsAFifthOfASecondEitherSide()
    {
        float[] audio = Audio(Pause(1), Speech(2), Pause(1.5), Speech(2), Pause(1));

        var phrases = VoiceSegments.Split(audio);

        // The first phrase starts at 1 s and ends at 3 s; padded, 0.8 s to 3.2 s.
        Assert.Equal((Rate * 4 / 5, Rate * 12 / 5), phrases[0]);
    }

    [Fact]
    public void Split_NoContrastBetweenSpeechAndBackground_KeepsTheWholeRecording()
    {
        float[] audio = Speech(5);

        Assert.Equal([(0, audio.Length)], VoiceSegments.Split(audio));
    }

    [Fact]
    public void Split_ABriefClickBetweenPhrases_IsLeftOut()
    {
        float[] audio = Audio(Speech(2), Pause(1), Speech(.05), Pause(1), Speech(2));

        Assert.Equal(2, VoiceSegments.Split(audio).Count);
    }

    [Theory]
    [InlineData(new[] { "先别开团", "等队友" }, "先别开团，等队友")]
    [InlineData(new[] { "先别开团。", "等队友" }, "先别开团。等队友")]
    [InlineData(new[] { "先别开团", "", "等队友" }, "先别开团，等队友")]
    [InlineData(new[] { " 推中 " }, "推中")]
    public void Join_Phrases_AddACommaOnlyWhereThereIsNoPunctuation(string[] phrases, string expected)
    {
        Assert.Equal(expected, VoiceSegments.Join(phrases));
    }
}
