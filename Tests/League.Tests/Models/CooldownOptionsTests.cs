using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The cooldown rules a player's recorded augments and hand-entered ultimate haste imply: the neutral instance other
/// types default to, the constants, the haste one ability's cooldown uses, Purist's cooldown share, the words the
/// options are reported in and the per-player reading of <c>ultimate-haste.json</c>. The expected values come from the
/// checks in Tests/StatisticsChecks/CooldownOptionChecks.cs.
/// </summary>
public class CooldownOptionsTests
{
    // The old suite's File/Haste helpers: one hand-entered ultimate haste per player, under a protocol and session.
    private static JObject HasteFile(string session, params (string Player, double Haste)[] players) =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = session,
            ["players"] = new JArray(
                players.Select(p => new JObject { ["player"] = p.Player, ["ultimateHaste"] = p.Haste })
            ),
        };

    [Fact]
    public void Constants_RecordedAugmentValues_MatchTheWiki()
    {
        Assert.Equal(100d, CooldownOptions.BreadHaste);
        Assert.Equal(0.9, CooldownOptions.PuristCooldown);
        Assert.Equal(100d, CooldownOptions.UltBotHaste);
        Assert.Equal(1.5, CooldownOptions.UltBotEffect);
        Assert.Equal(500d, CooldownOptions.MaxHaste);
    }

    [Fact]
    public void None_NoAugments_IsTheNeutralOption()
    {
        var none = CooldownOptions.None;

        Assert.Equal(new[] { false, false, false }, none.Bread);
        Assert.False(none.Purist);
        Assert.Equal("", none.Pursuit);
        Assert.Equal(0d, none.PursuitHaste);
        Assert.False(none.UltBot);
        Assert.Equal(0d, none.UltimateHaste);
        Assert.False(none.UltimateSealed);
        // Cooldowns are multiplied by this, so the neutral value is 1 and not 0.
        Assert.Equal(1d, none.Multiplier);
        Assert.Equal("", none.Summary());
    }

    [Theory]
    [InlineData(false, 1d)]
    [InlineData(true, 0.9)]
    public void Multiplier_PuristCaster_CutsCooldownsByTenPercent(bool purist, double expected)
    {
        var options = CooldownOptions.None with { Purist = purist };

        Assert.Equal(expected, options.Multiplier);
    }

    [Theory]
    [InlineData("Q")]
    [InlineData("W")]
    [InlineData("E")]
    [InlineData("R")]
    // A key the game has no slot for gets no ability-specific bonus and never reaches the ultimate branch.
    [InlineData("A")]
    public void Haste_NoAugments_ReturnsTheHasteItWasGiven(string key)
    {
        double haste = CooldownOptions.None.Haste(key, 40, estimated: false);

        Assert.Equal(40d, haste);
    }

    [Theory]
    // Ult Bot moves the general haste to the ultimate, so a basic ability keeps only its own haste, which is 0 here.
    [InlineData("Q", false, 0d, 60d)]
    // An estimate carries Ult Bot's 100 as well; 2999's own value already has it, so a measured one does not add it
    // again.
    [InlineData("Q", true, 0d, 60d)]
    [InlineData("R", false, 150d, 60d)]
    [InlineData("R", true, 300d, 60d)]
    // The measured haste, the hand-entered 30 and the 1.5 multiplier all show in the ultimate's total.
    [InlineData("R", false, 105d, 30d)]
    public void Haste_UltBot_MovesAbilityHasteToTheUltimateAtOneAndAHalfTimes(
        string key,
        bool estimated,
        double expected,
        double ultimateHaste
    )
    {
        var options = CooldownOptions.None with { UltBot = true, UltimateHaste = ultimateHaste };

        double haste = options.Haste(key, 40, estimated);

        Assert.Equal(expected, haste);
    }

    [Theory]
    [InlineData(0, 100d)]
    [InlineData(1, 100d)]
    [InlineData(2, 100d)]
    public void Haste_BreadAugment_AddsOneHundredToItsOwnAbility(int slot, double expected)
    {
        bool[] bread = [false, false, false];
        bread[slot] = true;
        var options = CooldownOptions.None with { Bread = bread };

        double haste = options.Haste("QWE"[slot].ToString(), 0, estimated: false);

        Assert.Equal(expected, haste);
    }

    [Theory]
    [InlineData("Q", 0d)]
    [InlineData("W", 160d)]
    [InlineData("E", 0d)]
    // Pursuit of Haste names one basic ability and adds nothing to the others.
    [InlineData("R", 0d)]
    public void Haste_PursuitOfHaste_AddsItsHasteToTheAbilityItChose(string key, double expected)
    {
        var options = CooldownOptions.None with { Pursuit = "W", PursuitHaste = 160 };

        double haste = options.Haste(key, 0, estimated: false);

        Assert.Equal(expected, haste);
    }

    [Theory]
    // Bread's 100 is the ability's own haste, so it is kept under Ult Bot while the haste the caller passed is not.
    [InlineData(40d, 100d)]
    [InlineData(0d, 100d)]
    public void Haste_BreadUnderUltBot_KeepsBreadAndDropsTheMeasuredGeneralHaste(double haste, double expected)
    {
        var options = CooldownOptions.None with { Bread = [true, false, false], UltBot = true };

        double result = options.Haste("Q", haste, estimated: false);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Haste_NegativeHaste_IsPassedThroughUnclamped()
    {
        double haste = CooldownOptions.None.Haste("Q", -40, estimated: false);

        Assert.Equal(-40d, haste);
    }

    [Fact]
    public void Haste_BackToBasicsSealed_StillReportsTheUltimatesHaste()
    {
        var options = CooldownOptions.None with { UltimateSealed = true, UltimateHaste = 30 };

        double haste = options.Haste("R", 0, estimated: false);

        Assert.Equal(30d, haste);
    }

    [Fact]
    public void Summary_NoOptionIsSet_IsEmpty()
    {
        string summary = CooldownOptions.None.Summary();

        Assert.Equal("", summary);
    }

    [Theory]
    [InlineData(true, false, false, "面包和黄油")]
    [InlineData(false, true, false, "面包和果酱")]
    [InlineData(false, false, true, "面包和奶酪")]
    [InlineData(true, true, false, "面包和黄油、面包和果酱")]
    [InlineData(true, true, true, "面包和黄油、面包和果酱、面包和奶酪")]
    public void Summary_BreadAugments_NameThemInSlotOrder(bool q, bool w, bool e, string expected)
    {
        var options = CooldownOptions.None with { Bread = [q, w, e] };

        Assert.Equal(expected, options.Summary());
    }

    [Theory]
    [InlineData("W", 80d, "急速之追求 W +80")]
    [InlineData("Q", 160d, "急速之追求 Q +160")]
    // The haste is rendered as a whole number.
    [InlineData("Q", 90.6, "急速之追求 Q +91")]
    public void Summary_PursuitOfHaste_NamesTheAbilityAndItsHaste(string pursuit, double haste, string expected)
    {
        var options = CooldownOptions.None with { Pursuit = pursuit, PursuitHaste = haste };

        Assert.Equal(expected, options.Summary());
    }

    [Theory]
    [InlineData(30d, "大招急速 30")]
    // The hand-entered value is rounded to a whole number when reported.
    [InlineData(45.6, "大招急速 46")]
    [InlineData(0.5, "大招急速 1")]
    [InlineData(0d, "")]
    [InlineData(-10d, "")]
    public void Summary_UltimateHaste_IsNamedOnlyWhenItIsPositive(double ultimateHaste, string expected)
    {
        var options = CooldownOptions.None with { UltimateHaste = ultimateHaste };

        Assert.Equal(expected, options.Summary());
    }

    [Fact]
    public void Summary_UltBotAndPuristAndSealed_NameEachRule()
    {
        var options = CooldownOptions.None with { UltBot = true, Purist = true, UltimateSealed = true };

        Assert.Equal("大招工具人、纯粹主义者 - 术师、大招已封印（回归基本功）", options.Summary());
    }

    [Fact]
    public void Summary_EveryOptionSet_JoinsThePartsWithTheIdeographicCommaInOrder()
    {
        var options = new CooldownOptions([true, false, false], true, "W", 160, true, 60) { UltimateSealed = true };

        Assert.Equal(
            "面包和黄油、急速之追求 W +160、大招工具人、大招急速 60、纯粹主义者 - 术师、大招已封印（回归基本功）",
            options.Summary()
        );
    }

    [Theory]
    [InlineData("Q", 40d, "急速之追求 Q +40")]
    [InlineData("W", 160d, "急速之追求 W +160")]
    [InlineData("W", 0d, "急速之追求 W +0")]
    // The pursuit is reported as it stands, even for a key the game has no slot for.
    [InlineData("A", 40d, "急速之追求 A +40")]
    public void Summary_PursuitOfHaste_NamesThePursuitAndItsHaste(string pursuit, double haste, string expected)
    {
        var options = CooldownOptions.None with { Pursuit = pursuit, PursuitHaste = haste };

        Assert.Equal(expected, options.Summary());
    }

    [Theory]
    [InlineData("", 160d)]
    [InlineData("", 0d)]
    public void Summary_PursuitWithoutAnAbility_IsNotReported(string pursuit, double haste)
    {
        var options = CooldownOptions.None with { Pursuit = pursuit, PursuitHaste = haste };

        Assert.Equal("", options.Summary());
    }

    [Fact]
    public void ParseUltimateHaste_HandEnteredValues_ReadsEachPlayer()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(
            HasteFile("s", ("a", 30), ("b", 0)),
            "s"
        );

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(30d, parsed["a"]);
        Assert.Equal(0d, parsed["b"]);
    }

    [Fact]
    public void ParseUltimateHaste_SamePlayerTwice_IsRefused()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(
            HasteFile("s", ("a", 1), ("a", 2)),
            "s"
        );

        Assert.Null(parsed);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-1d)]
    [InlineData(500.1)]
    [InlineData(1000d)]
    public void ParseUltimateHaste_HasteOutsideZeroToMax_IsRefused(double haste)
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(HasteFile("s", ("a", haste)), "s");

        Assert.Null(parsed);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(500d)]
    public void ParseUltimateHaste_HasteOnEitherBoundary_IsRead(double haste)
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(HasteFile("s", ("a", haste)), "s");

        Assert.NotNull(parsed);
        Assert.Equal(haste, parsed["a"]);
    }

    [Theory]
    [InlineData(0d)]
    // 1, 2 and 3.5 are not whole numbers of ultimate haste.
    [InlineData(1.5)]
    [InlineData(2.25)]
    [InlineData(3.5)]
    public void ParseUltimateHaste_FractionalHaste_IsReadAsEntered(double haste)
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(HasteFile("s", ("a", haste)), "s");

        Assert.NotNull(parsed);
        Assert.Equal(haste, parsed["a"]);
    }

    [Theory]
    [InlineData("s", "old")]
    [InlineData("old", "s")]
    public void ParseUltimateHaste_OtherSessionsFile_IsRefused(string fileSession, string session)
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(
            HasteFile(fileSession, ("a", 1)),
            session
        );

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_EmptySession_IsRefused()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(HasteFile("", ("a", 1)), "");

        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("one")]
    public void ParseUltimateHaste_ProtocolIsNotTheIntegerOne_IsRefused(string protocol)
    {
        var value = HasteFile("s", ("a", 1));
        value["protocol"] = protocol;

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_MissingProtocol_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        value.Remove("protocol");

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_MissingSession_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        value.Remove("session");

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_PlayersNotAnArray_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        value["players"] = new JObject { ["player"] = "a" };

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    // A game has at most ten players, so an eleventh entry means the file is not one.
    private static JObject ElevenPlayers() =>
        HasteFile(
            "s",
            ("p1", 1),
            ("p2", 1),
            ("p3", 1),
            ("p4", 1),
            ("p5", 1),
            ("p6", 1),
            ("p7", 1),
            ("p8", 1),
            ("p9", 1),
            ("p10", 1),
            ("p11", 1)
        );

    [Fact]
    public void ParseUltimateHaste_TenPlayers_ReadsThemAll()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(
            HasteFile(
                "s",
                ("p1", 1),
                ("p2", 1),
                ("p3", 1),
                ("p4", 1),
                ("p5", 1),
                ("p6", 1),
                ("p7", 1),
                ("p8", 1),
                ("p9", 1),
                ("p10", 1)
            ),
            "s"
        );

        Assert.NotNull(parsed);
        Assert.Equal(10, parsed.Count);
        Assert.Equal(1d, parsed["p10"]);
    }

    [Fact]
    public void ParseUltimateHaste_ElevenPlayers_IsRefused()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(ElevenPlayers(), "s");

        Assert.Null(parsed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void ParseUltimateHaste_PlayerNameOfAtMostTwoHundredAndFiftySixCharacters_IsRead(int length)
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(
            HasteFile("s", (new string('a', length), 1)),
            "s"
        );

        Assert.NotNull(parsed);
        Assert.Equal(1d, parsed[new string('a', length)]);
    }

    [Fact]
    public void ParseUltimateHaste_PlayerNameOfTwoHundredAndFiftySevenCharacters_IsRefused()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(
            HasteFile("s", (new string('a', 257), 1)),
            "s"
        );

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_EmptyPlayerName_IsRefused()
    {
        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(HasteFile("s", ("", 1)), "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_PlayerNotAString_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        value["players"]![0]!["player"] = 7;

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("")]
    public void ParseUltimateHaste_HasteThatIsAString_IsRefused(string haste)
    {
        var value = HasteFile("s", ("a", 1));
        value["players"]![0]!["ultimateHaste"] = new JValue(haste);

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_HasteThatIsABoolean_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        value["players"]![0]!["ultimateHaste"] = new JValue(true);

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_HasteThatIsNull_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        value["players"]![0]!["ultimateHaste"] = JValue.CreateNull();

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseUltimateHaste_MissingHaste_IsRefused()
    {
        var value = HasteFile("s", ("a", 1));
        (value["players"]![0] as JObject)!.Remove("ultimateHaste");

        Dictionary<string, double>? parsed = CooldownOptions.ParseUltimateHaste(value, "s");

        Assert.Null(parsed);
    }
}
