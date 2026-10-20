using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The recording lease the widget hands the main window: start, stop or cancel, and an acknowledgement number.
///
/// This protocol is deliberately narrow. It carries no text and no confirmation, so a request that has either is
/// refused rather than ignored - the comment in the source says it never authorizes chat input, and refusing is how
/// that is enforced. The timestamps are checked against two windows: three seconds since the last update, because the
/// lease is live, and a minute since it was created, because a lease does not last longer than that.
/// </summary>
public class OverlayVoiceRequestTests
{
    private const long Now = 100_000;

    private static JObject Request(string action = "start") =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = "s",
            ["id"] = Guid.NewGuid().ToString(),
            ["action"] = action,
            ["createdAt"] = Now - 1_000,
            ["updatedAt"] = Now - 1_000,
            ["ack"] = 0,
        };

    private static OverlayVoiceRequest? Parse(JObject value, long now = Now, string session = "s") =>
        OverlayVoiceRequest.Parse(value, session, now);

    // ---- the actions it carries ----

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("cancel")]
    public void Parse_AKnownAction_IsAccepted(string action)
    {
        Assert.Equal(action, Parse(Request(action))?.Action);
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("pause")]
    [InlineData("")]
    public void Parse_AnActionOutsideTheThree_IsRefused(string action)
    {
        Assert.Null(Parse(Request(action)));
    }

    // ---- the protocol and the session ----

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
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Parse_AnIdThatIsNotAnId_IsRefused(string id)
    {
        JObject value = Request();
        value["id"] = id;

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_TheIdentifier_IsWrittenAsThirtyTwoDigits()
    {
        // The id is a GUID in the payload and a bare hex string once parsed, which is what the widget matches on.
        Guid id = Guid.NewGuid();
        JObject value = Request();
        value["id"] = id.ToString();

        Assert.Equal(id.ToString("N"), Parse(value)?.Id);
    }

    // ---- the fields this protocol does not carry ----

    [Fact]
    public void Parse_TextAlongsideTheRecording_IsRefused()
    {
        // The comment on this type says it never authorizes chat input; refusing is how that is kept true.
        JObject value = Request();
        value["text"] = "集合打龙";

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AConfirmationAlongsideTheRecording_IsRefused()
    {
        JObject value = Request();
        value["confirmed"] = true;

        Assert.Null(Parse(value));
    }

    // ---- the two clocks ----

    [Fact]
    public void Parse_AnUpdateJustInsideTheLease_IsAccepted()
    {
        // Three seconds is the lease; a request older than that is stale.
        JObject value = Request();
        value["createdAt"] = Now - 3_000;
        value["updatedAt"] = Now - 3_000;

        Assert.NotNull(Parse(value));
    }

    [Fact]
    public void Parse_AnUpdateOlderThanTheLease_IsRefused()
    {
        JObject value = Request();
        value["createdAt"] = Now - 4_000;
        value["updatedAt"] = Now - 3_001;

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AnUpdateInTheFuture_IsRefused()
    {
        JObject value = Request();
        value["updatedAt"] = Now + 1;

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_AnUpdateBeforeTheCreation_IsRefused()
    {
        // A lease cannot be updated before it was created.
        JObject value = Request();
        value["createdAt"] = Now - 1_000;
        value["updatedAt"] = Now - 2_000;

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_ACreationJustInsideTheMinute_IsAccepted()
    {
        // The lease itself does not outlive a minute, whatever the updates say.
        JObject value = Request();
        value["createdAt"] = Now - 60_000;
        value["updatedAt"] = Now - 1_000;

        Assert.NotNull(Parse(value));
    }

    [Fact]
    public void Parse_ACreationOlderThanAMinute_IsRefused()
    {
        JObject value = Request();
        value["createdAt"] = Now - 60_001;
        value["updatedAt"] = Now - 1_000;

        Assert.Null(Parse(value));
    }

    [Fact]
    public void Parse_ACreationBeforeTheGame_IsRefused()
    {
        JObject value = Request();
        value["createdAt"] = -1;
        value["updatedAt"] = -1;

        Assert.Null(Parse(value));
    }

    // ---- the acknowledgement ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void Parse_AnAcknowledgementInRange_IsAccepted(long ack)
    {
        JObject value = Request();
        value["ack"] = ack;

        Assert.Equal(ack, Parse(value)?.Ack);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Parse_AnAcknowledgementOutOfRange_IsRefused(long ack)
    {
        JObject value = Request();
        value["ack"] = ack;

        Assert.Null(Parse(value));
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    [InlineData("ack")]
    public void Parse_ACountThatIsNotACount_IsRefused(string field)
    {
        JObject value = Request();
        value[field] = "soon";

        Assert.Null(Parse(value));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("session")]
    [InlineData("id")]
    [InlineData("action")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    [InlineData("ack")]
    public void Parse_AMissingField_IsRefused(string field)
    {
        JObject value = Request();
        value.Remove(field);

        Assert.Null(Parse(value));
    }

    // ---- telling one recording from another ----

    [Fact]
    public void SameRecording_TheSameLease_IsTheSameRecording()
    {
        // Only the identity of the lease matters; an acknowledgement moving on is the same recording.
        OverlayVoiceRequest one = new("i", "s", 1, 2, "start", Ack: 0);
        OverlayVoiceRequest other = one with { Ack = 5, Action = "stop", UpdatedAt = 9 };

        Assert.True(one.SameRecording(other));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Session")]
    [InlineData("CreatedAt")]
    public void SameRecording_ADifferentLease_IsADifferentRecording(string member)
    {
        OverlayVoiceRequest one = new("i", "s", 1, 2, "start", Ack: 0);
        OverlayVoiceRequest other = member switch
        {
            "Id" => one with { Id = "j" },
            "Session" => one with { Session = "t" },
            _ => one with { CreatedAt = 2 },
        };

        Assert.False(one.SameRecording(other));
    }
}
