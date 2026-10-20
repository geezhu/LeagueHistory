using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// Summoner spell cooldowns and the timers the user starts on an ally.
///
/// The cooldown is the spell's own, replaced by the mode's override where the game data has one, and then shortened by
/// both the player's summoner haste and the haste the mode gives everyone. The timer file is a small protocol of its
/// own: anything malformed makes the whole file unusable rather than being skipped entry by entry, so a timer is never
/// shown for the wrong player.
/// </summary>
public class SummonerSpellsTests
{
    private static SummonerSpellInfo Spell(string id = "SummonerFlash", double cooldown = 300) =>
        new(id, "闪现", cooldown, "flash.png");

    private static LiveMatchItem Item(int id) => new(id, "装备", 1);

    private static JObject Timers(params JObject[] rows) =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = "s",
            ["timers"] = new JArray(rows),
        };

    private static JObject Row(string player = "p1", int slot = 0, double time = 12.5) =>
        new()
        {
            ["player"] = player,
            ["slot"] = slot,
            ["time"] = time,
        };

    // ---- the internal name ----

    [Theory]
    [InlineData("GeneratedTip_SummonerSpell_SummonerFlash_DisplayName", "SummonerFlash")]
    [InlineData("GeneratedTip_SummonerSpell_SummonerHeal_DisplayName", "SummonerHeal")]
    public void InternalName_ARawNameInTheGamesForm_IsUnwrapped(string raw, string expected)
    {
        Assert.Equal(expected, SummonerSpells.InternalName(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("闪现")]
    [InlineData("GeneratedTip_SummonerSpell_SummonerFlash")]
    [InlineData("SummonerFlash_DisplayName")]
    [InlineData("generatedtip_summonerspell_SummonerFlash_DisplayName")]
    public void InternalName_AnythingElse_IsEmpty(string? raw)
    {
        // The wrapping is matched exactly, so a name in another shape is not guessed at.
        Assert.Equal("", SummonerSpells.InternalName(raw));
    }

    // ---- summoner haste from items ----

    [Fact]
    public void Haste_NoItemsAndNoRunes_IsNothing()
    {
        Assert.Equal(0, SummonerSpells.Haste([], []));
    }

    [Theory]
    [InlineData(3158, 10)]
    [InlineData(3171, 20)]
    [InlineData(3020, 0)]
    public void Haste_TheTwoLucidityBoots_GiveTheirHaste(int itemId, double expected)
    {
        Assert.Equal(expected, SummonerSpells.Haste([Item(itemId)], []));
    }

    [Fact]
    public void Haste_TheSameItemTwice_CountsTwice()
    {
        // Items are summed as they come; the caller is what decides which it passes.
        Assert.Equal(30, SummonerSpells.Haste([Item(3158), Item(3171)], []));
    }

    [Theory]
    [InlineData(103158)]
    [InlineData(203158)]
    public void Haste_AnItemIdCarryingAnotherNumberInItsHighPart_IsReadByItsLowPart(int itemId)
    {
        // Some ids arrive with extra digits in front; the item is the last four.
        Assert.Equal(10, SummonerSpells.Haste([Item(itemId)], []));
    }

    [Fact]
    public void Haste_CosmicInsight_AddsItsShare()
    {
        Assert.Equal(18, SummonerSpells.Haste([], [8347]));
    }

    [Fact]
    public void Haste_AnotherRune_AddsNothing()
    {
        // Only the rune whose effect is reported is counted; other minor runes are not visible.
        Assert.Equal(0, SummonerSpells.Haste([], [8000, 9100]));
    }

    [Fact]
    public void Haste_ItemsAndTheRune_AddUp()
    {
        Assert.Equal(28, SummonerSpells.Haste([Item(3158)], [8347]));
    }

    // ---- the haste the mode gives ----

    [Theory]
    [InlineData("aram", 70)]
    [InlineData("kiwi", 70)]
    [InlineData("urf", 300)]
    [InlineData("classic", 0)]
    [InlineData("", 0)]
    public void ModeHaste_EachMode_GivesWhatItsAuraGives(string mode, double expected)
    {
        Assert.Equal(expected, SummonerSpells.ModeHaste(mode));
    }

    [Fact]
    public void ModeHaste_TheModeNameIsMatchedWhateverItsCase()
    {
        Assert.Equal(70, SummonerSpells.ModeHaste("ARAM"));
    }

    // ---- the base cooldown ----

    [Fact]
    public void BaseCooldown_AnOverrideForTheMode_IsUsed()
    {
        var modes = new Dictionary<string, Dictionary<string, double>> { ["SummonerFlash"] = new() { ["aram"] = 200 } };

        Assert.Equal(200, SummonerSpells.BaseCooldown(Spell(cooldown: 300), "aram", modes));
    }

    [Fact]
    public void BaseCooldown_TheModeIsMatchedLowerCase()
    {
        var modes = new Dictionary<string, Dictionary<string, double>> { ["SummonerFlash"] = new() { ["aram"] = 200 } };

        Assert.Equal(200, SummonerSpells.BaseCooldown(Spell(cooldown: 300), "ARAM", modes));
    }

    [Fact]
    public void BaseCooldown_NoOverrideForThisSpell_FallsBackToItsOwn()
    {
        var modes = new Dictionary<string, Dictionary<string, double>> { ["SummonerFlash"] = new() { ["aram"] = 200 } };

        Assert.Equal(240, SummonerSpells.BaseCooldown(Spell("SummonerHeal", 240), "aram", modes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BaseCooldown_AnOverrideThatIsNotACooldown_IsIgnored(double overrideValue)
    {
        var modes = new Dictionary<string, Dictionary<string, double>>
        {
            ["SummonerFlash"] = new() { ["aram"] = overrideValue },
        };

        Assert.Equal(300, SummonerSpells.BaseCooldown(Spell(cooldown: 300), "aram", modes));
    }

    [Fact]
    public void BaseCooldown_NoModesAtAll_FallsBackToTheSpellsOwn()
    {
        Assert.Equal(300, SummonerSpells.BaseCooldown(Spell(cooldown: 300), "aram", null));
    }

    // ---- the cooldown after haste ----

    [Fact]
    public void Cooldown_NoHaste_IsTheBase()
    {
        Assert.Equal(300, SummonerSpells.Cooldown(Spell(), 0), 3);
    }

    [Fact]
    public void Cooldown_ThePlayersHaste_ShortensIt()
    {
        // A hundred haste halves it: 300 * 100 / 200.
        Assert.Equal(150, SummonerSpells.Cooldown(Spell(), 100), 3);
    }

    [Fact]
    public void Cooldown_TheModesHaste_IsAddedToThePlayers()
    {
        // ARAM's aura gives 70 to everyone, on top of the player's own.
        Assert.Equal(300d * 100 / 170, SummonerSpells.Cooldown(Spell(), 0, "aram"), 3);
        Assert.Equal(300d * 100 / 270, SummonerSpells.Cooldown(Spell(), 100, "aram"), 3);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(-1)]
    public void Cooldown_NegativeHaste_CountsAsNone(double haste)
    {
        // A cooldown never grows because of a nonsense haste value.
        Assert.Equal(300, SummonerSpells.Cooldown(Spell(), haste), 3);
    }

    [Fact]
    public void Cooldown_TheModeOverrideFeedsIntoTheHastedNumber()
    {
        var modes = new Dictionary<string, Dictionary<string, double>> { ["SummonerFlash"] = new() { ["aram"] = 200 } };

        Assert.Equal(200d * 100 / 170, SummonerSpells.Cooldown(Spell(), 0, "aram", modes), 3);
    }

    // ---- a running timer ----

    [Theory]
    [InlineData(0.0, 0, 300.0, 300.0)]
    [InlineData(50.0, 0, 300.0, 350.0)]
    [InlineData(null, 0, 300.0, null)]
    public void ReadyAt_IsWhereTheTimerEnds(double? started, int slot, double cooldown, double? expected)
    {
        PlayerSummoner summoner = new(slot, Spell(), cooldown, started);

        Assert.Equal(expected, summoner.ReadyAt);
    }

    // ---- the timer file ----

    [Fact]
    public void ParseTimers_AWellFormedFile_ReadsEveryTimer()
    {
        SummonerTimer[]? timers = SummonerSpells.ParseTimers(
            Timers(Row("p1", 0, 12.5), Row("p1", 1, 20), Row("p2", 0, 0)),
            "s"
        );

        Assert.Equal(3, timers?.Length);
        Assert.Equal(new SummonerTimer("p1", 0, 12.5), timers![0]);
    }

    [Fact]
    public void ParseTimers_NoTimers_IsAnEmptyListNotAFailure()
    {
        Assert.Empty(SummonerSpells.ParseTimers(Timers(), "s")!);
    }

    [Fact]
    public void ParseTimers_AnIntegerTime_IsAccepted()
    {
        // The file may hold either; a whole second is stored as an integer.
        JObject row = new()
        {
            ["player"] = "p1",
            ["slot"] = 0,
            ["time"] = 12,
        };

        Assert.Equal(12, Assert.Single(SummonerSpells.ParseTimers(Timers(row), "s")!).Time);
    }

    [Theory]
    [InlineData("protocol", 2)]
    [InlineData("protocol", "1")]
    [InlineData("session", "other")]
    [InlineData("timers", "not an array")]
    public void ParseTimers_AFieldThatDoesNotMatch_IsNull(string field, object wrong)
    {
        JObject value = Timers(Row());
        value[field] = JToken.FromObject(wrong);

        Assert.Null(SummonerSpells.ParseTimers(value, "s"));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("session")]
    [InlineData("timers")]
    public void ParseTimers_AMissingField_IsNull(string field)
    {
        JObject value = Timers(Row());
        value.Remove(field);

        Assert.Null(SummonerSpells.ParseTimers(value, "s"));
    }

    [Fact]
    public void ParseTimers_AnEmptySessionName_IsNull()
    {
        // A file with no session cannot be matched to this widget, so none of it can be trusted.
        JObject value = Timers(Row());
        value["session"] = "";
        value["protocol"] = 1;

        Assert.Null(SummonerSpells.ParseTimers(value, ""));
    }

    [Fact]
    public void ParseTimers_TooManyTimers_IsNull()
    {
        // Ten players hold two spells each; more rows than that is not this widget's file.
        var rows = Enumerable.Range(0, 21).Select(i => Row($"p{i}", 0, 1)).ToArray();

        Assert.Null(SummonerSpells.ParseTimers(Timers(rows), "s"));
    }

    [Fact]
    public void ParseTimers_TwentyTimers_IsStillAccepted()
    {
        // Exactly the cap is allowed, so the limit is not one short.
        var rows = Enumerable.Range(0, 20).Select(i => Row($"p{i}", 0, 1)).ToArray();

        Assert.Equal(20, SummonerSpells.ParseTimers(Timers(rows), "s")!.Length);
    }

    [Theory]
    [InlineData("player", 5)]
    [InlineData("slot", "0")]
    [InlineData("time", "12")]
    public void ParseTimers_ARowOfTheWrongShape_IsNull(string field, object wrong)
    {
        JObject row = Row();
        row[field] = JToken.FromObject(wrong);

        Assert.Null(SummonerSpells.ParseTimers(Timers(row), "s"));
    }

    [Fact]
    public void ParseTimers_AnEmptyPlayerName_IsNull()
    {
        Assert.Null(SummonerSpells.ParseTimers(Timers(Row("", 0, 1)), "s"));
    }

    [Fact]
    public void ParseTimers_APlayerNameThatIsTooLong_IsNull()
    {
        Assert.Null(SummonerSpells.ParseTimers(Timers(Row(new string('名', 257), 0, 1)), "s"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ParseTimers_ASlotThatIsNotOneOfTheTwo_IsNull(int slot)
    {
        Assert.Null(SummonerSpells.ParseTimers(Timers(Row("p1", slot, 1)), "s"));
    }

    [Theory]
    [InlineData(-1)]
    public void ParseTimers_ATimeBeforeTheGame_IsNull(double time)
    {
        Assert.Null(SummonerSpells.ParseTimers(Timers(Row("p1", 0, time)), "s"));
    }

    [Fact]
    public void ParseTimers_ANonFiniteTime_IsNull()
    {
        JObject row = Row();
        row["time"] = double.PositiveInfinity;

        Assert.Null(SummonerSpells.ParseTimers(Timers(row), "s"));
    }

    [Fact]
    public void ParseTimers_TheSamePlayersSameSlotTwice_IsNull()
    {
        // One player has each spell once; a second row for the same slot is a file this widget did not write.
        Assert.Null(SummonerSpells.ParseTimers(Timers(Row("p1", 0, 1), Row("p1", 0, 2)), "s"));
    }

    [Fact]
    public void ParseTimers_TheSamePlayersOtherSlot_IsFine()
    {
        Assert.Equal(2, SummonerSpells.ParseTimers(Timers(Row("p1", 0, 1), Row("p1", 1, 2)), "s")!.Length);
    }

    [Fact]
    public void ParseTimers_OneBadRow_SpoilsTheWholeFile()
    {
        // Rejecting the file rather than the row is what keeps a timer from being shown against the wrong player.
        Assert.Null(SummonerSpells.ParseTimers(Timers(Row("p1", 0, 1), Row("", 0, 1)), "s"));
    }
}
