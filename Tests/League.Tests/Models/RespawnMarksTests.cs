using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The marks the Game Bar user made for respawn reminders, as the widget writes them to a file.
///
/// The two lists are parallel: slot i is the snapshot row and hero i is the champion the user saw there, so a slot
/// whose champion has since changed is not mistaken for the one they meant. That pairing is why the two counts have to
/// agree - a file where they disagree cannot be read at all rather than half read.
/// </summary>
public class RespawnMarksTests
{
    private const int MaxSlots = RespawnMarks.MaxSlots;

    private static JObject Marks(int[]? slots = null, int[]? heroes = null, bool sound = false, long test = 0)
    {
        slots ??= [1, 2];
        heroes ??= [11, 12];
        return new JObject
        {
            ["protocol"] = 1,
            ["session"] = "s",
            ["slots"] = new JArray(slots.Cast<object>().ToArray()),
            ["heroes"] = new JArray(heroes.Cast<object>().ToArray()),
            ["sound"] = sound,
            ["test"] = test,
        };
    }

    private static RespawnMarks? Parse(JObject value, string session = "s") => RespawnMarks.Parse(value, session);

    // ---- the cap ----

    [Fact]
    public void MaxSlots_IsTen()
    {
        Assert.Equal(10, MaxSlots);
    }

    // ---- a well-formed file ----

    [Fact]
    public void Parse_AWellFormedFile_ReadsBothListsAndTheFlags()
    {
        RespawnMarks? marks = Parse(Marks([1, 2], [11, 12], sound: true, test: 7));

        Assert.NotNull(marks);
        Assert.Equal([1, 2], marks.Slots);
        Assert.Equal([11, 12], marks.Heroes);
        Assert.True(marks.Sound);
        Assert.Equal(7, marks.Test);
        Assert.Equal("s", marks.Session);
    }

    [Fact]
    public void Parse_NoMarks_IsAnEmptyPair()
    {
        // Nothing marked is a valid file, not a missing one.
        RespawnMarks? marks = Parse(Marks([], []));

        Assert.NotNull(marks);
        Assert.Empty(marks.Slots);
    }

    [Fact]
    public void Parse_TenMarks_IsStillAccepted()
    {
        // Exactly the cap is allowed, so the limit is not one short.
        Assert.NotNull(Parse(Marks([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10])));
    }

    // ---- what makes it unreadable ----

    [Fact]
    public void Parse_ElevenMarks_IsRefused()
    {
        // There are only ten rows in the snapshot, so an eleventh mark cannot refer to one.
        Assert.Null(Parse(Marks([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])));
    }

    [Fact]
    public void Parse_TheTwoListsOfDifferentLengths_IsRefused()
    {
        // The pairing is what makes the file meaningful; without it there is no way to read it.
        Assert.Null(Parse(Marks([1, 2], [11])));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(99)]
    public void Parse_ASlotOutsideTheTenRows_IsRefused(int slot)
    {
        Assert.Null(Parse(Marks([slot], [11])));
    }

    [Fact]
    public void Parse_ASlotAndAHeroThatAreNotNumbers_IsRefused()
    {
        JObject value = Marks();
        value["slots"] = new JArray("one");
        value["heroes"] = new JArray(11);

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AListThatIsNotAList_IsRefused()
    {
        JObject value = Marks();
        value["slots"] = "not a list";

        Assert.Null(Parse(value));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("session")]
    [InlineData("slots")]
    [InlineData("heroes")]
    [InlineData("sound")]
    [InlineData("test")]
    public void Parse_AMissingField_IsRefused(string field)
    {
        JObject value = Marks();
        value.Remove(field);

        Assert.Null(Parse(value));
    }

    [Theory]
    [InlineData("protocol", 2)]
    [InlineData("protocol", "1")]
    [InlineData("session", "other")]
    [InlineData("sound", "false")]
    [InlineData("test", "0")]
    public void Parse_AFieldOfTheWrongShape_IsRefused(string field, object wrong)
    {
        JObject value = Marks();
        value[field] = JToken.FromObject(wrong);

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AnEmptySessionName_IsRefused()
    {
        // A file that cannot be matched to this widget is not this widget's file.
        JObject value = Marks();
        value["session"] = "";

        Assert.Null(Parse(value, session: ""));
    }
}
