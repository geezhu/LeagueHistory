using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The augments the user recorded by hand, as the widget writes them to a file. 2999 does not report augments, so the
/// file is the only record, and it is trusted only when it is entirely well formed.
///
/// That is the shape of this parser: one bad entry rejects the whole file rather than dropping that entry. The reason
/// is that a partial read would silently show fewer augments than the user recorded, and they would have no way to
/// tell the difference between that and the program having lost them.
///
/// A choice is kept only when the augment has one by that name; the choices come from the catalog, so an augment with
/// no choices must carry an empty one.
/// </summary>
public class AugmentSelectionsTests
{
    private const int MaxStacks = AugmentSelections.MaxStacks;

    /// <summary>An augment whose catalog entry has no choices.</summary>
    private static readonly TrackedAugment Fixed = AugmentCatalog.All.First(i => i.Choices.Length == 0).Augment;

    /// <summary>An augment whose catalog entry needs a choice, and the first key it accepts.</summary>
    private static readonly TrackedAugmentInfo Chosen = AugmentCatalog.All.First(i => i.Choices.Length > 0);

    private static JObject Row(
        TrackedAugment augment,
        string player = "p",
        double time = 100,
        int stacks = 3,
        string choice = ""
    ) =>
        new()
        {
            ["player"] = player,
            ["augment"] = augment.ToString(),
            ["time"] = time,
            ["stacks"] = stacks,
            ["choice"] = choice,
        };

    private static JObject File(params JObject[] rows) =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = "s",
            ["augments"] = new JArray(rows.Cast<object>().ToArray()),
        };

    private static AugmentSelection[]? Parse(JObject value, string session = "s") =>
        AugmentSelections.Parse(value, session);

    // ---- a well-formed file ----

    [Fact]
    public void Parse_AWellFormedFile_IsRead()
    {
        AugmentSelection[]? parsed = Parse(File(Row(Fixed, player: "p", time: 120.5, stacks: 7)));

        Assert.NotNull(parsed);
        AugmentSelection selection = Assert.Single(parsed);
        Assert.Equal("p", selection.Player);
        Assert.Equal(Fixed, selection.Augment);
        Assert.Equal(120.5, selection.Anchor.Time, 3);
        Assert.Equal(7, selection.Anchor.Stacks);
    }

    [Fact]
    public void Parse_NoAugments_IsAnEmptyArrayRatherThanNothing()
    {
        // An empty file is a valid file; it says the user has recorded nothing yet.
        AugmentSelection[]? parsed = Parse(File());

        Assert.NotNull(parsed);
        Assert.Empty(parsed);
    }

    [Fact]
    public void Parse_TheSameAugmentForTwoPlayers_IsAllowed()
    {
        // Two players can both have it.
        AugmentSelection[]? parsed = Parse(File(Row(Fixed, player: "a"), Row(Fixed, player: "b")));

        Assert.Equal(2, parsed!.Length);
    }

    [Fact]
    public void Parse_AnEmptyPlayerKey_IsAllowed()
    {
        // The widget writes an empty key for its own player.
        Assert.NotNull(Parse(File(Row(Fixed, player: ""))));
    }

    [Fact]
    public void Parse_NoPlayerFieldAtAll_IsAllowedAndReadsAsEmpty()
    {
        // The player is optional for the same reason: the widget's own record has nobody else to name.
        JObject row = Row(Fixed);
        row.Remove("player");

        AugmentSelection selection = Assert.Single(Parse(File(row))!);
        Assert.Equal("", selection.Player);
    }

    [Fact]
    public void MaxStacks_IsNineHundredNinetyNine()
    {
        Assert.Equal(999, MaxStacks);
    }

    // ---- the choices ----

    [Fact]
    public void Parse_AnAugmentWithChoices_CarryingOneOfThem_IsAccepted()
    {
        Assert.NotNull(Parse(File(Row(Chosen.Augment, choice: Chosen.Choices[0].Key))));
    }

    [Fact]
    public void Parse_AnAugmentWithChoices_CarryingNoChoice_IsAccepted()
    {
        // The empty choice means "the first one"; the loader fills it in.
        Assert.NotNull(Parse(File(Row(Chosen.Augment, choice: ""))));
    }

    [Fact]
    public void Parse_AnAugmentWithChoices_CarryingOneItDoesNotHave_IsRefused()
    {
        Assert.Null(Parse(File(Row(Chosen.Augment, choice: "not-a-choice"))));
    }

    [Fact]
    public void Parse_AnAugmentWithoutChoices_CarryingOne_IsRefused()
    {
        // It has none to choose from, so a choice is a sign the file is not what it claims.
        Assert.Null(Parse(File(Row(Fixed, choice: "anything"))));
    }

    // ---- values that are out of range ----

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Parse_StacksOutsideTheRange_AreRefused(int stacks)
    {
        Assert.Null(Parse(File(Row(Fixed, stacks: stacks))));
    }

    [Fact]
    public void Parse_TheHighestStackCount_IsAccepted()
    {
        Assert.NotNull(Parse(File(Row(Fixed, stacks: MaxStacks))));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void Parse_ATimeBeforeTheGame_IsRefused(double time)
    {
        Assert.Null(Parse(File(Row(Fixed, time: time))));
    }

    [Fact]
    public void Parse_TheSameAugmentTwiceForOnePlayer_IsRefused()
    {
        // There is no way to tell which record the user meant, so the file is not trusted at all.
        Assert.Null(Parse(File(Row(Fixed, player: "a"), Row(Fixed, player: "a"))));
    }

    // ---- the handshake and the shapes ----

    [Theory]
    [InlineData(2)]
    [InlineData("1")]
    public void Parse_AProtocolThatIsNotOne_IsRefused(object protocol)
    {
        JObject value = File();
        value["protocol"] = JToken.FromObject(protocol);

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AnotherSessionsFile_IsRefused()
    {
        Assert.Null(Parse(File(Row(Fixed)), session: "other"));
    }

    [Fact]
    public void Parse_AnEmptySession_IsRefused()
    {
        JObject value = File();
        value["session"] = "";

        Assert.Null(Parse(value, session: ""));
    }

    [Fact]
    public void Parse_NoAugmentList_IsRefused()
    {
        var value = new JObject { ["protocol"] = 1, ["session"] = "s" };

        Assert.Null(Parse(value));
    }

    [Theory]
    [InlineData("augment")]
    [InlineData("time")]
    [InlineData("stacks")]
    public void Parse_AMissingField_IsRefused(string field)
    {
        JObject row = Row(Fixed);
        row.Remove(field);

        Assert.Null(Parse(File(row)));
    }

    [Theory]
    [InlineData("augment", "NotAnAugment")]
    [InlineData("stacks", "three")]
    [InlineData("time", "soon")]
    public void Parse_AFieldOfTheWrongShape_IsRefused(string field, string wrong)
    {
        JObject row = Row(Fixed);
        row[field] = wrong;

        Assert.Null(Parse(File(row)));
    }

    [Fact]
    public void Parse_APlayerKeyThatIsTooLong_IsRefused()
    {
        // The key is a roster identifier; a long string is not one.
        Assert.Null(Parse(File(Row(Fixed, player: new string('x', 257)))));
    }

    [Fact]
    public void Parse_ARowThatIsNotAnObject_IsRefused()
    {
        JObject value = File();
        value["augments"] = new JArray("not an object");

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_MoreRowsThanThereArePlayersAndAugments_IsRefused()
    {
        // The cap is ten players times the number of augments; beyond it the file cannot be a real one.
        int cap = 10 * Enum.GetValues<TrackedAugment>().Length;
        var rows = new JArray();
        for (int i = 0; i <= cap; i++)
        {
            rows.Add(Row(Fixed, player: "p" + i));
        }

        JObject value = File();
        value["augments"] = rows;

        Assert.Null(Parse(value));
    }
}
