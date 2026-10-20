using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The widget's chat requests, written as the widget writes them.
///
/// <c>Parse</c> is the gatekeeper every overlay chat message goes through, so its refusals are pinned one reason at
/// a time: a command id that does not match the session, a timestamp outside the 30-second window, a kind outside the
/// known list, a target that does not suit the kind, and the spell rule that only summoner requests may use.
/// </summary>
public class OverlayChatRequestTests
{
    private const long Now = 100_000;

    private static JObject Request(string kind, string target = "enemy", int slot = -1, int hero = 0) =>
        new()
        {
            ["protocol"] = 1,
            ["id"] = Guid.NewGuid().ToString(),
            ["session"] = "s",
            ["target"] = target,
            ["kind"] = kind,
            ["slot"] = slot,
            ["championId"] = hero,
            ["updatedAt"] = 1000,
            ["dismissed"] = false,
            ["cancelled"] = false,
        };

    private static JObject RequestWithoutKind(string target = "enemy", int slot = -1, int hero = 0)
    {
        JObject value = Request("tier", target, slot, hero);
        value.Remove("kind");
        return value;
    }

    private static int[] Seats(params (int Seat, int Champion)[] filled)
    {
        int[] heroes = new int[10];
        foreach ((int seat, int champion) in filled)
        {
            heroes[seat] = champion;
        }

        return heroes;
    }

    // ---- the command id gate ----

    [Fact]
    public void Parse_HardControlsForASide_KeepsTheKind()
    {
        var request = OverlayChatRequest.Parse(Request("hardControls"), "s", new int[10], 2000);

        Assert.Equal("hardControls", request?.Kind);
    }

    [Fact]
    public void Parse_HardControlsForOneHero_IsRefused()
    {
        int[] heroes = [5, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var request = OverlayChatRequest.Parse(Request("hardControls", "single", 0, 5), "s", heroes, 2000);

        Assert.Null(request);
    }

    [Fact]
    public void Parse_ASessionThatIsNotOurs_IsRefused()
    {
        // The widget and the receiver share a session string; a request for another session is not ours to run.
        var request = OverlayChatRequest.Parse(Request("tier"), "another-session", new int[10], 1000);

        Assert.Null(request);
    }

    [Theory]
    [InlineData("protocol", 2)]
    [InlineData("protocol", "1")]
    [InlineData("id", "not-a-guid")]
    [InlineData("target", 5)]
    [InlineData("updatedAt", "1000")]
    [InlineData("slot", "0")]
    [InlineData("championId", "0")]
    [InlineData("dismissed", "false")]
    [InlineData("cancelled", 0)]
    public void Parse_AFieldOfTheWrongShape_IsRefused(string field, object wrong)
    {
        // Each field is type-checked rather than coerced, so a stringified number is not accepted as a number.
        JObject value = Request("tier");
        value[field] = JToken.FromObject(wrong);

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("id")]
    [InlineData("session")]
    [InlineData("target")]
    [InlineData("updatedAt")]
    [InlineData("slot")]
    [InlineData("championId")]
    [InlineData("dismissed")]
    [InlineData("cancelled")]
    public void Parse_AMissingField_IsRefused(string field)
    {
        JObject value = Request("tier");
        value.Remove(field);

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    // ---- the freshness window ----

    [Theory]
    [InlineData(29_999, true)]
    [InlineData(30_000, true)]
    [InlineData(30_001, false)]
    public void Parse_TheThirtySecondWindow_IsInclusiveAtItsEdge(int age, bool accepted)
    {
        JObject value = Request("tier");
        value["updatedAt"] = Now - age;

        var request = OverlayChatRequest.Parse(value, "s", new int[10], Now);

        Assert.Equal(accepted, request != null);
    }

    [Fact]
    public void Parse_ATimestampInTheFuture_IsRefused()
    {
        JObject value = Request("tier");
        value["updatedAt"] = Now + 1;

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], Now));
    }

    [Fact]
    public void Parse_ANegativeTimestamp_IsRefused()
    {
        JObject value = Request("tier");
        value["updatedAt"] = -1;

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], Now));
    }

    // ---- the kind list ----

    [Theory]
    [InlineData("tier")]
    [InlineData("mastery")]
    [InlineData("deathStreak")]
    [InlineData("record")]
    [InlineData("party")]
    [InlineData("controls")]
    [InlineData("hardControls")]
    [InlineData("composition")]
    public void Parse_AKnownKindNamingASide_IsAccepted(string kind)
    {
        // waves and summoner are known too, but they carry their own required fields and are covered below.
        var request = OverlayChatRequest.Parse(Request(kind), "s", new int[10], 1000);

        Assert.Equal(kind, request?.Kind);
    }

    [Theory]
    [InlineData("TIER")]
    [InlineData("")]
    [InlineData("unknown")]
    public void Parse_AKindOutsideTheList_IsRefused(string kind)
    {
        Assert.Null(OverlayChatRequest.Parse(Request(kind), "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_NoKindAtAll_IsTreatedAsTier()
    {
        var request = OverlayChatRequest.Parse(RequestWithoutKind(), "s", new int[10], 1000);

        Assert.Equal("tier", request?.Kind);
    }

    // ---- the target rule, which differs by kind ----

    [Theory]
    [InlineData("party")]
    [InlineData("controls")]
    [InlineData("hardControls")]
    [InlineData("composition")]
    public void Parse_ASideKindNamingOneHero_IsRefused(string kind)
    {
        int[] heroes = Seats((0, 5));

        Assert.Null(OverlayChatRequest.Parse(Request(kind, "single", 0, 5), "s", heroes, 1000));
    }

    [Theory]
    [InlineData("party")]
    [InlineData("controls")]
    [InlineData("hardControls")]
    [InlineData("composition")]
    public void Parse_ASideKindNamingASide_IsAccepted(string kind)
    {
        var request = OverlayChatRequest.Parse(Request(kind, "own"), "s", new int[10], 1000);

        Assert.Equal("own", request?.Target);
    }

    [Fact]
    public void Parse_OneHeroOnASeatThatHoldsThatHero_IsAccepted()
    {
        int[] heroes = Seats((3, 77));

        var request = OverlayChatRequest.Parse(Request("tier", "single", 3, 77), "s", heroes, 1000);

        Assert.Equal(3, request?.Slot);
        Assert.Equal(77, request?.ChampionId);
    }

    [Theory]
    [InlineData(-1, 77)]
    [InlineData(10, 77)]
    [InlineData(3, 0)]
    [InlineData(3, 78)]
    public void Parse_OneHeroWithASeatOrHeroThatDoesNotMatch_IsRefused(int slot, int hero)
    {
        // 0 means "no champion on that seat", and a hero that is not the one the seat holds is not the one named.
        int[] heroes = Seats((3, 77));

        Assert.Null(OverlayChatRequest.Parse(Request("tier", "single", slot, hero), "s", heroes, 1000));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("")]
    public void Parse_ASideKindWithATargetThatIsNeitherSide_IsRefused(string target)
    {
        Assert.Null(OverlayChatRequest.Parse(Request("tier", target), "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_AKindThatMayNameOneHeroStillAcceptsASide()
    {
        // The same kinds accept either a side or one hero.
        var request = OverlayChatRequest.Parse(Request("tier", "enemy"), "s", Seats((0, 5)), 1000);

        Assert.Equal("enemy", request?.Target);
        Assert.Equal(-1, request?.Slot);
    }

    [Theory]
    [InlineData("spawn")]
    [InlineData("super")]
    [InlineData("deaths")]
    public void Parse_AWaveKindNamingAWaveContent_IsAccepted(string target)
    {
        var request = OverlayChatRequest.Parse(Request("waves", target), "s", new int[10], 1000);

        Assert.Equal(target, request?.Target);
    }

    [Fact]
    public void Parse_AWaveKindCarryingASeat_IsRefused()
    {
        // A wave message describes the whole game, so a seat is meaningless and must be -1.
        Assert.Null(OverlayChatRequest.Parse(Request("waves", "spawn", slot: 2), "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_AWaveKindCarryingAHero_IsRefused()
    {
        Assert.Null(OverlayChatRequest.Parse(Request("waves", "spawn", hero: 5), "s", new int[10], 1000));
    }

    // ---- the spell rule ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Parse_ASummonerRequestForOneHero_CarriesTheSpell(int spell)
    {
        JObject value = Request("summoner", "single", 3, 77);
        value["spell"] = spell;

        var request = OverlayChatRequest.Parse(value, "s", Seats((3, 77)), 1000);

        Assert.Equal(spell, request?.Spell);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData("0")]
    public void Parse_ASummonerRequestWithASpellOutsideZeroOrOne_IsRefused(object spell)
    {
        JObject value = Request("summoner", "single", 3, 77);
        value["spell"] = JToken.FromObject(spell);

        Assert.Null(OverlayChatRequest.Parse(value, "s", Seats((3, 77)), 1000));
    }

    [Fact]
    public void Parse_ASummonerRequestNamingASide_IsRefused()
    {
        // The spell belongs to the selected hero, so the request has to name one.
        JObject value = Request("summoner", "own");
        value["spell"] = 0;

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_ASummonerRequestWithNoSpell_IsRefused()
    {
        Assert.Null(OverlayChatRequest.Parse(Request("summoner", "single", 3, 77), "s", Seats((3, 77)), 1000));
    }

    [Fact]
    public void Parse_AnyOtherKindCarryingASpell_IsRefused()
    {
        JObject value = Request("tier");
        value["spell"] = 0;

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_AnyOtherKindWithoutASpell_ReportsMinusOne()
    {
        // -1 is the "no spell" value; it is not the same as an absent field being refused.
        var request = OverlayChatRequest.Parse(Request("tier"), "s", new int[10], 1000);

        Assert.Equal(-1, request?.Spell);
    }

    // ---- the augment list ----

    private static JObject AugmentRequest(JToken augments, string target = "single", int slot = 3, int hero = 77)
    {
        JObject value = Request("augments", target, slot, hero);
        value["augments"] = augments;
        return value;
    }

    [Fact]
    public void Parse_AnAugmentRequestForTheDetailHero_KeepsThePickedOrder()
    {
        JObject value = AugmentRequest(new JArray(1205, 1001, 2072));

        var request = OverlayChatRequest.Parse(value, "s", Seats((3, 77)), 1000);

        Assert.Equal([1205, 1001, 2072], request?.Augments);
    }

    [Fact]
    public void Parse_AnAugmentRequestNamingASide_IsRefused()
    {
        // The rates belong to one hero, so the request names the hero on the detail page.
        JObject value = AugmentRequest(new JArray(1001), "own", -1, 0);

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_AnAugmentRequestForAHeroNotInThatSeat_IsRefused()
    {
        JObject value = AugmentRequest(new JArray(1001));

        Assert.Null(OverlayChatRequest.Parse(value, "s", Seats((3, 78)), 1000));
    }

    public static TheoryData<JToken> MalformedAugmentLists =>
        new()
        {
            new JArray(),
            new JArray(1001, 1001),
            new JArray(1001, 0),
            new JArray(1001, -5),
            new JArray(1001, "1002"),
            new JArray(1001, 1.5),
            new JArray(1001, 3_000_000_000L),
            new JArray(Enumerable.Range(1001, OverlayChatRequest.MaxAugments + 1)),
            "1001",
            JValue.CreateNull(),
        };

    [Theory]
    [MemberData(nameof(MalformedAugmentLists))]
    public void Parse_AnAugmentListThatIsEmptyRepeatedOrMalformed_IsRefused(JToken augments)
    {
        Assert.Null(OverlayChatRequest.Parse(AugmentRequest(augments), "s", Seats((3, 77)), 1000));
    }

    [Fact]
    public void Parse_AnAugmentRequestWithoutTheList_IsRefused()
    {
        Assert.Null(OverlayChatRequest.Parse(Request("augments", "single", 3, 77), "s", Seats((3, 77)), 1000));
    }

    [Fact]
    public void Parse_AnAugmentListOfTheMaximumLength_IsAccepted()
    {
        JObject value = AugmentRequest(new JArray(Enumerable.Range(1001, OverlayChatRequest.MaxAugments)));

        var request = OverlayChatRequest.Parse(value, "s", Seats((3, 77)), 1000);

        Assert.Equal(OverlayChatRequest.MaxAugments, request?.Augments?.Count);
    }

    [Fact]
    public void Parse_AnyOtherKindCarryingAugments_IsRefused()
    {
        JObject value = Request("tier");
        value["augments"] = new JArray(1001);

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    // ---- voice ----

    [Fact]
    public void Parse_VoiceTextThatIsConfirmed_IsAcceptedAndCarriesTheText()
    {
        JObject value = Request("voice", "text");
        value["confirmed"] = true;
        value["text"] = "  集合打龙  ";

        var request = OverlayChatRequest.Parse(value, "s", new int[10], 1000);

        Assert.True(request?.IsVoice, "A voice request should report itself as one.");
        Assert.Equal("集合打龙", request?.Text);
        Assert.True(request?.Confirmed);
    }

    [Fact]
    public void Parse_NonVoiceKindsCarryingText_AreRefused()
    {
        // text and confirmed exist for voice alone; a tier request carrying them is malformed.
        JObject value = Request("tier");
        value["text"] = "集合打龙";

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    [Theory]
    [InlineData("own")]
    [InlineData("single")]
    public void Parse_VoiceWithATargetOtherThanText_IsRefused(string target)
    {
        JObject value = Request("voice", target);
        value["confirmed"] = true;
        value["text"] = "集合打龙";

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    [Fact]
    public void Parse_VoiceThatIsNotConfirmed_IsRefused()
    {
        JObject value = Request("voice", "text");
        value["confirmed"] = false;
        value["text"] = "集合打龙";

        Assert.Null(OverlayChatRequest.Parse(value, "s", new int[10], 1000));
    }

    // ---- the record's own members ----

    [Theory]
    [InlineData("voice", true, true)]
    [InlineData("waves", false, true)]
    [InlineData("tier", false, false)]
    public void IsVoiceAndIsRosterFree_FollowTheKind(string kind, bool voice, bool rosterFree)
    {
        var request = new OverlayChatRequest("i", "s", "own", -1, 0, 1, false, false, kind);

        Assert.Equal(voice, request.IsVoice);
        Assert.Equal(rosterFree, request.IsRosterFree);
    }

    [Fact]
    public void SameCommand_TwoRequestsDifferingOnlyInDismissed_AreTheSameCommand()
    {
        // The widget's dismissal is its own business; it does not make it a different command.
        var one = new OverlayChatRequest("i", "s", "own", -1, 0, 1, false, false);
        var other = one with { Dismissed = true, Cancelled = true };

        Assert.True(one.SameCommand(other), "Dismissal and cancellation are not part of the command's identity.");
    }

    [Theory]
    [InlineData("Kind")]
    [InlineData("Target")]
    [InlineData("Slot")]
    [InlineData("ChampionId")]
    [InlineData("CreatedAt")]
    [InlineData("Text")]
    [InlineData("Confirmed")]
    [InlineData("Spell")]
    [InlineData("Augments")]
    [InlineData("Id")]
    [InlineData("Session")]
    public void SameCommand_ARequestDifferingInOneMember_IsADifferentCommand(string member)
    {
        var one = new OverlayChatRequest("i", "s", "own", 1, 5, 1, false, false, "tier", "t", false, 0);
        var other = member switch
        {
            "Kind" => one with { Kind = "record" },
            "Target" => one with { Target = "enemy" },
            "Slot" => one with { Slot = 2 },
            "ChampionId" => one with { ChampionId = 6 },
            "CreatedAt" => one with { CreatedAt = 2 },
            "Text" => one with { Text = "u" },
            "Confirmed" => one with { Confirmed = true },
            "Spell" => one with { Spell = 1 },
            "Augments" => one with { Augments = [1001] },
            "Id" => one with { Id = "j" },
            _ => one with { Session = "t" },
        };

        Assert.False(one.SameCommand(other), $"{member} is part of the command's identity.");
    }
}
