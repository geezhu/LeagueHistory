using System.Text;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Respawn mark chimes: which transition plays which tone, the sound switch, the sample chime the marks file asks
/// for when its counter changes, and the WAV tones. Every instance gets a callback of its own, so no test reaches
/// the system-sound fallback and nothing plays on the machine's audio device. The expected values come from the
/// checks in Tests/StatisticsChecks/RespawnMarkChecks.cs.
/// </summary>
public class RespawnMarkSoundsTests
{
    private const string Neeko = "neeko";
    private const string Ahri = "ahri";
    private const long Counter = 7;
    private const int HeaderBytes = 44;

    [Fact]
    public void Update_ALivingChampionInsideTheWindow_IsSilent()
    {
        var mark = new RespawnMarkState(Neeko, Dead: false, Seconds: 1);
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // The fixture has to put the timer inside the window, or silence would only mean "not due yet".
        Assert.Equal(true, mark.Seconds <= RespawnMarkSounds.SoonSeconds);

        sounds.Update([mark], true, Counter);
        sounds.Update([mark], true, Counter);

        Assert.Empty(played);
    }

    [Fact]
    public void Update_AFirstMarkedChampionInsideTheWindow_IsSilent()
    {
        const double seconds = 2;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // Already inside the window on the first sighting: the silence must come from the first sighting itself.
        Assert.Equal(true, seconds <= RespawnMarkSounds.SoonSeconds);

        sounds.Update([new RespawnMarkState(Neeko, true, seconds)], true, Counter);

        Assert.Empty(played);
    }

    [Fact]
    public void Update_AKnownDeathWithALongTimer_IsSilent()
    {
        const double dying = 23;
        const double oneSecondLater = 22;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // Both timers have to be outside the window, or a chime would be the five-second warning, not the death.
        Assert.Equal(true, dying > RespawnMarkSounds.SoonSeconds);
        Assert.Equal(true, oneSecondLater > RespawnMarkSounds.SoonSeconds);

        sounds.Update([new RespawnMarkState(Neeko, false, 0)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, dying)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, oneSecondLater)], true, Counter);

        Assert.Empty(played);
    }

    [Fact]
    public void Update_AKnownLivingChampionDyingInsideTheWindow_ChimesAtOnce()
    {
        const double seconds = 4;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // A death this short never crosses the boundary later, so the warning has to come with the death itself.
        Assert.Equal(true, seconds <= RespawnMarkSounds.SoonSeconds);

        sounds.Update([new RespawnMarkState(Neeko, false, 0)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, seconds)], true, Counter);

        Assert.Equal(new[] { RespawnTone.Soon }, played);
    }

    [Theory]
    [InlineData(6.0, 5.0)]
    [InlineData(6.0, 4.999)]
    [InlineData(5.001, 5.0)]
    [InlineData(6.0, 0.0)]
    public void Update_AKnownDeathCrossingTheSoonWindow_ChimesOnce(double before, double after)
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // The fixture has to straddle the window: outside before the poll, inside at it.
        Assert.Equal(true, before > RespawnMarkSounds.SoonSeconds);
        Assert.Equal(true, after <= RespawnMarkSounds.SoonSeconds);

        sounds.Update([new RespawnMarkState(Neeko, true, before)], true, Counter);
        int afterFirstPoll = played.Count;
        sounds.Update([new RespawnMarkState(Neeko, true, after)], true, Counter);

        Assert.Equal(0, afterFirstPoll);
        Assert.Equal(new[] { RespawnTone.Soon }, played);
    }

    [Fact]
    public void Update_ATimerStoppingJustAboveTheSoonWindow_StaysSilentWhereTheBoundaryChimes()
    {
        const double justAbove = 5.001;
        const double outside = 6;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        sounds.Update([new RespawnMarkState(Neeko, true, outside)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, justAbove)], true, Counter);

        // The control runs the same two polls a thousandth of a second lower, where the boundary is reached.
        var control = new List<RespawnTone>();
        using var atBoundary = new RespawnMarkSounds(control.Add);
        atBoundary.Update([new RespawnMarkState(Neeko, true, outside)], true, Counter);
        atBoundary.Update([new RespawnMarkState(Neeko, true, RespawnMarkSounds.SoonSeconds)], true, Counter);

        Assert.Equal(true, justAbove > RespawnMarkSounds.SoonSeconds);
        Assert.Empty(played);
        Assert.Equal(new[] { RespawnTone.Soon }, control);
    }

    [Fact]
    public void Update_TheSameDeathOnLaterPolls_ChimesOnce()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        sounds.Update([new RespawnMarkState(Neeko, true, 6)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, 5)], true, Counter);
        int afterWarning = played.Count;
        sounds.Update([new RespawnMarkState(Neeko, true, 3)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, 0.5)], true, Counter);

        Assert.Equal(1, afterWarning);
        Assert.Empty(played.Skip(afterWarning));
    }

    [Fact]
    public void Update_TheSameDeathWithTheTimerRisingAgain_DoesNotChimeTwice()
    {
        const double inside = 5;
        const double longAgain = 30;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // A missed alive poll leaves the key dead across two deaths; the timer has to be inside for the latch to arm.
        Assert.Equal(true, inside <= RespawnMarkSounds.SoonSeconds);
        Assert.Equal(true, longAgain > RespawnMarkSounds.SoonSeconds);

        sounds.Update([new RespawnMarkState(Neeko, true, inside)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, longAgain)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, inside)], true, Counter);

        Assert.Empty(played);
    }

    [Fact]
    public void Update_ARevivedChampionDyingAgain_ChimesForTheNewDeath()
    {
        const double shortDeath = 3;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // The second death is as short as the first, so a fresh warning is due; only a reset death state stays mute.
        Assert.Equal(true, shortDeath <= RespawnMarkSounds.SoonSeconds);

        sounds.Update([new RespawnMarkState(Neeko, true, shortDeath)], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, false, 0)], true, Counter);
        int afterRevive = played.Count;
        sounds.Update([new RespawnMarkState(Neeko, true, shortDeath)], true, Counter);

        Assert.Equal(new[] { RespawnTone.Revive }, played.Take(afterRevive));
        Assert.Equal(new[] { RespawnTone.Soon }, played.Skip(afterRevive));
    }

    [Fact]
    public void Update_TwoMarkedChampions_KeepTheirOwnDeathsAndRevives()
    {
        const double death = 4;
        const double outside = 6;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // One poll, one tone per champion, in the order the poll reports them.
        Assert.Equal(true, death <= RespawnMarkSounds.SoonSeconds);
        Assert.Equal(true, outside > RespawnMarkSounds.SoonSeconds);

        sounds.Update(
            [new RespawnMarkState(Neeko, false, 0), new RespawnMarkState(Ahri, true, outside)],
            true,
            Counter
        );
        int afterMarking = played.Count;
        sounds.Update([new RespawnMarkState(Neeko, true, death), new RespawnMarkState(Ahri, false, 0)], true, Counter);

        Assert.Equal(0, afterMarking);
        Assert.Equal(new[] { RespawnTone.Soon, RespawnTone.Revive }, played);
    }

    [Fact]
    public void Update_AChampionMissingFromThePoll_IsForgottenSoItsNextDeathStartsSilent()
    {
        const double outside = 6;
        const double inside = 5;
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        sounds.Update([new RespawnMarkState(Neeko, true, outside)], true, Counter);
        sounds.Update([], true, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, inside)], true, Counter);

        // The control keeps the champion marked, so the same crossing chimes instead of starting over.
        var control = new List<RespawnTone>();
        using var remembered = new RespawnMarkSounds(control.Add);
        remembered.Update([new RespawnMarkState(Neeko, true, outside)], true, Counter);
        remembered.Update([new RespawnMarkState(Neeko, true, inside)], true, Counter);

        Assert.Equal(true, outside > RespawnMarkSounds.SoonSeconds);
        Assert.Equal(true, inside <= RespawnMarkSounds.SoonSeconds);
        Assert.Empty(played);
        Assert.Equal(new[] { RespawnTone.Soon }, control);
    }

    [Fact]
    public void Update_SoundOff_PlaysNothingWhereTheSamePollsChimeWithSoundOn()
    {
        var played = new List<RespawnTone>();
        using var muted = new RespawnMarkSounds(played.Add);
        muted.Update([new RespawnMarkState(Neeko, true, 6)], false, Counter);
        muted.Update([new RespawnMarkState(Neeko, true, 5)], false, Counter);
        muted.Update([new RespawnMarkState(Neeko, false, 0)], false, Counter);

        // The control shows the very same three polls do reach the callback when sound is on.
        var control = new List<RespawnTone>();
        using var sounding = new RespawnMarkSounds(control.Add);
        sounding.Update([new RespawnMarkState(Neeko, true, 6)], true, Counter);
        sounding.Update([new RespawnMarkState(Neeko, true, 5)], true, Counter);
        sounding.Update([new RespawnMarkState(Neeko, false, 0)], true, Counter);

        Assert.Empty(played);
        Assert.Equal(new[] { RespawnTone.Soon, RespawnTone.Revive }, control);
    }

    [Fact]
    public void Update_SoundTurnedBackOn_DoesNotReplayTheWarningMutedBeforeIt()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);
        sounds.Update([new RespawnMarkState(Neeko, true, 6)], false, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, 5)], false, Counter);
        sounds.Update([new RespawnMarkState(Neeko, true, 3)], true, Counter);

        // The control was never muted: it chimes at the crossing and stays silent once the warning has been played.
        var control = new List<RespawnTone>();
        using var sounding = new RespawnMarkSounds(control.Add);
        sounding.Update([new RespawnMarkState(Neeko, true, 6)], true, Counter);
        sounding.Update([new RespawnMarkState(Neeko, true, 5)], true, Counter);
        sounding.Update([new RespawnMarkState(Neeko, true, 3)], true, Counter);

        Assert.Empty(played);
        Assert.Equal(new[] { RespawnTone.Soon }, control);
    }

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(7L, 8L)]
    [InlineData(4_000_000_000L, 4_000_000_001L)]
    public void Update_TheMarksCounterChanging_PlaysOneSampleWhereTheFirstFileIsSilent(long first, long second)
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        // The same empty poll chimes on the second counter, so the silence of the first is not an empty list at work.
        Assert.Equal(true, first != second);

        sounds.Update([], true, first);
        int afterFirstFile = played.Count;
        sounds.Update([], true, second);

        Assert.Equal(0, afterFirstFile);
        Assert.Equal(new[] { RespawnTone.Soon }, played);
    }

    [Fact]
    public void Update_TheSameCounterOnEveryPoll_DoesNotSampleUntilItChanges()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);

        sounds.Update([], true, Counter);
        sounds.Update([], true, Counter);
        int afterRepeated = played.Count;
        sounds.Update([], true, Counter + 1);

        Assert.Equal(0, afterRepeated);
        Assert.Equal(new[] { RespawnTone.Soon }, played);
    }

    [Fact]
    public void Update_ACounterChangeWhileMuted_DoesNotSampleWhenSoundComesBack()
    {
        var played = new List<RespawnTone>();
        using var muted = new RespawnMarkSounds(played.Add);
        muted.Update([], false, 0);
        muted.Update([], false, 1);
        muted.Update([], true, 1);

        // The control changed the counter while sound was on, which is the one case that samples.
        var control = new List<RespawnTone>();
        using var sounding = new RespawnMarkSounds(control.Add);
        sounding.Update([], true, 0);
        sounding.Update([], true, 1);

        Assert.Empty(played);
        Assert.Equal(new[] { RespawnTone.Soon }, control);
    }

    [Fact]
    public void Reset_TheNextFileIsSilentAndTheFollowingCounterChangeSamples()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);
        sounds.Update([], true, 0);
        sounds.Update([], true, 1);
        int afterSample = played.Count;

        sounds.Reset();
        sounds.Update([], true, 2);
        int afterReset = played.Count;
        sounds.Update([], true, 3);

        Assert.Equal(1, afterSample);
        Assert.Equal(1, afterReset);
        Assert.Equal(new[] { RespawnTone.Soon }, played.Skip(afterReset));
    }

    [Fact]
    public void Reset_AChampionThatWasDead_IsNotRevivedOnTheNextPoll()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);
        sounds.Update([new RespawnMarkState(Neeko, true, 3)], true, Counter);

        sounds.Reset();
        sounds.Update([new RespawnMarkState(Neeko, false, 0)], true, Counter);

        // The control never reset: the pair alive-after-dead is a revive there.
        var control = new List<RespawnTone>();
        using var kept = new RespawnMarkSounds(control.Add);
        kept.Update([new RespawnMarkState(Neeko, true, 3)], true, Counter);
        kept.Update([new RespawnMarkState(Neeko, false, 0)], true, Counter);

        Assert.Empty(played);
        Assert.Equal(new[] { RespawnTone.Revive }, control);
    }

    [Theory]
    [InlineData(0, 44)]
    [InlineData(1, 7540)]
    [InlineData(2, 15036)]
    [InlineData(3, 22532)]
    public void Tone_NoteCount_SetsTheWaveLength(int notes, int bytes)
    {
        // 44 header bytes plus 130 ms of note and a 40 ms gap per note: (2866 + 882) samples of 2 bytes.
        byte[] wave = RespawnMarkSounds.Tone(new double[notes]);

        Assert.Equal(bytes, wave.Length);
        Assert.Equal(bytes - HeaderBytes, BitConverter.ToInt32(wave, 40));
    }

    [Fact]
    public void Tone_TwoNotesAtDifferentFrequencies_KeepTheSameLength()
    {
        byte[] warning = RespawnMarkSounds.Tone(988);
        byte[] lower = RespawnMarkSounds.Tone(659.25);

        Assert.Equal(0, lower.Length - warning.Length);
    }

    [Fact]
    public void Tone_Header_DescribesA16BitMonoWaveAt22050Hz()
    {
        byte[] wave = RespawnMarkSounds.Tone(660, 990);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wave, 8, 8));
        Assert.Equal(16, BitConverter.ToInt32(wave, 16));
        Assert.Equal(1, BitConverter.ToInt16(wave, 20));
        Assert.Equal(1, BitConverter.ToInt16(wave, 22));
        Assert.Equal(22050, BitConverter.ToInt32(wave, 24));
        Assert.Equal(44100, BitConverter.ToInt32(wave, 28));
        Assert.Equal(2, BitConverter.ToInt16(wave, 32));
        Assert.Equal(16, BitConverter.ToInt16(wave, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
        Assert.Equal(wave.Length - HeaderBytes, BitConverter.ToInt32(wave, 40));
    }

    [Fact]
    public void SummonerReadyWave_Value_IsACompleteWave()
    {
        byte[] wave = RespawnMarkSounds.SummonerReadyWave.Value;

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal(true, wave.Length > HeaderBytes);
        Assert.Equal(wave.Length - HeaderBytes, BitConverter.ToInt32(wave, 40));
        Assert.Same(wave, RespawnMarkSounds.SummonerReadyWave.Value);
    }

    [Fact]
    public void CannonWave_Value_IsACompleteWave()
    {
        byte[] wave = RespawnMarkSounds.CannonWave.Value;

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal(true, wave.Length > HeaderBytes);
        Assert.Equal(wave.Length - HeaderBytes, BitConverter.ToInt32(wave, 40));
        Assert.Same(wave, RespawnMarkSounds.CannonWave.Value);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var played = new List<RespawnTone>();
        var sounds = new RespawnMarkSounds(played.Add);
        sounds.Update([new RespawnMarkState(Neeko, true, 5)], true, Counter);

        sounds.Dispose();
        sounds.Dispose();

        Assert.Empty(played);
    }
}
