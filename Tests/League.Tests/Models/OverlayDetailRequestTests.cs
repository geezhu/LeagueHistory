using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The request the Game Bar sends when the user asks for a roster slot's details.
///
/// It is deliberately narrow: a slot index and a champion id, nothing that could address a file or an account. The
/// champion id is a guard rather than a parameter - it must match what the widget believes is in that slot, so a
/// request that raced a roster change is refused instead of answering about the wrong player.
///
/// One shape clears the panel rather than asking for something: slot -1 with champion 0.
/// </summary>
public class OverlayDetailRequestTests
{
    private const long Now = 100_000;

    private static readonly int[] Heroes = [11, 12, 13, 14, 15, 21, 22, 23, 24, 25];

    private static JObject Request(int slot = 0, int championId = 11, long? updatedAt = null, string? id = null)
    {
        return new JObject
        {
            ["protocol"] = 1,
            ["session"] = "s",
            ["id"] = id ?? Guid.NewGuid().ToString(),
            ["updatedAt"] = updatedAt ?? (Now - 1_000),
            ["slot"] = slot,
            ["championId"] = championId,
        };
    }

    private static OverlayDetailRequest? Parse(
        JObject value,
        long now = Now,
        string session = "s",
        int[]? heroes = null
    ) => OverlayDetailRequest.Parse(value, session, heroes ?? Heroes, now);

    // ---- a request for a slot ----

    [Fact]
    public void Parse_ACurrentSlot_IsAccepted()
    {
        OverlayDetailRequest? request = Parse(Request(slot: 2, championId: 13));

        Assert.NotNull(request);
        Assert.Equal(2, request.Slot);
        Assert.Equal(13, request.ChampionId);
        Assert.Equal("s", request.Session);
    }

    [Fact]
    public void Parse_ASlotWhoseChampionDoesNotMatch_IsRefused()
    {
        // The roster may have changed since the widget read it; answering about the new occupant would be wrong.
        Assert.Null(Parse(Request(slot: 2, championId: 99)));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(10)]
    [InlineData(100)]
    public void Parse_ASlotOutsideTheRoster_IsRefused(int slot)
    {
        Assert.Null(Parse(Request(slot: slot)));
    }

    [Fact]
    public void Parse_AChampionOfZeroOnARealSlot_IsRefused()
    {
        // An empty slot has no details to ask for.
        Assert.Null(Parse(Request(slot: 0, championId: 0)));
    }

    // ---- the request that clears the panel ----

    [Fact]
    public void Parse_TheClearingShape_IsAccepted()
    {
        // Slot -1 with champion 0 is the widget saying there is nothing selected any more.
        OverlayDetailRequest? request = Parse(Request(slot: -1, championId: 0));

        Assert.NotNull(request);
        Assert.Equal(-1, request.Slot);
        Assert.Equal(0, request.ChampionId);
    }

    [Fact]
    public void Parse_SlotMinusOneWithAChampion_IsRefused()
    {
        // The two halves of the clearing shape go together.
        Assert.Null(Parse(Request(slot: -1, championId: 11)));
    }

    // ---- the identifier and the clock ----

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Parse_AnIdentifierThatIsNotAnIdentifier_IsRefused(string id)
    {
        Assert.Null(Parse(Request(id: id)));
    }

    [Fact]
    public void Parse_ARequestJustInsideTheHalfMinute_IsAccepted()
    {
        // Thirty seconds is how stale a request may be; older than that the roster has likely moved on.
        Assert.NotNull(Parse(Request(updatedAt: Now - 30_000)));
    }

    [Fact]
    public void Parse_ARequestOlderThanTheHalfMinute_IsRefused()
    {
        Assert.Null(Parse(Request(updatedAt: Now - 30_001)));
    }

    [Fact]
    public void Parse_ARequestFromTheFuture_IsRefused()
    {
        Assert.Null(Parse(Request(updatedAt: Now + 1)));
    }

    [Fact]
    public void Parse_AStampBeforeTheGame_IsRefused()
    {
        Assert.Null(Parse(Request(updatedAt: -1)));
    }

    // ---- the handshake ----

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData("1")]
    public void Parse_AProtocolThatIsNotOne_IsRefused(object protocol)
    {
        JObject value = Request();
        value["protocol"] = JToken.FromObject(protocol);

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AnotherSessionsRequest_IsRefused()
    {
        Assert.Null(Parse(Request(), session: "other"));
    }

    [Fact]
    public void Parse_AnEmptySession_IsRefused()
    {
        JObject value = Request();
        value["session"] = "";

        Assert.Null(Parse(value, session: ""));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("session")]
    [InlineData("id")]
    [InlineData("updatedAt")]
    [InlineData("slot")]
    [InlineData("championId")]
    public void Parse_AMissingField_IsRefused(string field)
    {
        JObject value = Request();
        value.Remove(field);

        Assert.Null(Parse(value));
    }

    [Theory]
    [InlineData("slot", "0")]
    [InlineData("championId", "11")]
    [InlineData("updatedAt", "soon")]
    public void Parse_AFieldOfTheWrongShape_IsRefused(string field, string wrong)
    {
        JObject value = Request();
        value[field] = wrong;

        Assert.Null(Parse(value));
    }
}
