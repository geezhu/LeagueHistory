using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The text of a confirmed voice request, and the one chat message it turns into.
///
/// <c>TryNormalize</c> is deliberately strict: it refuses anything that could not be sent as a single line, so the
/// length limit applies to the raw input (before trimming), control characters and the two line separators are
/// refused rather than replaced, and a lone half of a surrogate pair is refused instead of being cut.
/// </summary>
public class OverlayVoiceChatTextTests
{
    private static OverlayChatRequest Voice(
        string text,
        bool confirmed = true,
        string target = "text",
        int slot = -1,
        int championId = 0,
        string kind = "voice"
    ) =>
        new(
            Id: "i",
            Session: "s",
            Target: target,
            Slot: slot,
            ChampionId: championId,
            CreatedAt: 1,
            Dismissed: false,
            Cancelled: false,
            Kind: kind,
            Text: text,
            Confirmed: confirmed
        );

    // ---- TryNormalize ----

    [Fact]
    public void TryNormalize_AnOrdinaryLine_IsKept()
    {
        Assert.True(OverlayVoiceChatText.TryNormalize("集合打龙", out string text));
        Assert.Equal("集合打龙", text);
    }

    [Fact]
    public void TryNormalize_SurroundingWhitespace_IsTrimmed()
    {
        Assert.True(OverlayVoiceChatText.TryNormalize("  集合打龙  ", out string text));
        Assert.Equal("集合打龙", text);
    }

    [Fact]
    public void TryNormalize_Null_IsRefused()
    {
        Assert.False(OverlayVoiceChatText.TryNormalize(null, out string text));
        Assert.Equal("", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryNormalize_EmptyOrOnlyWhitespace_IsRefused(string input)
    {
        // Whitespace is a control character, so "\t" is refused before the trim even runs.
        Assert.False(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void TryNormalize_ExactlyTheMaximumLength_IsKept()
    {
        string input = new('a', OverlayVoiceChatText.MaxLength);

        Assert.Equal(180, input.Length);
        Assert.True(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal(input, text);
    }

    [Fact]
    public void TryNormalize_OneCharacterOverTheMaximum_IsRefused()
    {
        string input = new('a', OverlayVoiceChatText.MaxLength + 1);

        Assert.False(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void TryNormalize_LongerThanTheMaximumOnlyBecauseOfSurroundingSpaces_IsStillRefused()
    {
        // The limit is measured on the raw input, before the trim, so padding cannot buy room.
        string input = " " + new string('a', OverlayVoiceChatText.MaxLength) + " ";

        Assert.Equal(182, input.Length);
        Assert.False(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal("", text);
    }

    [Theory]
    [InlineData("集合\n打龙")]
    [InlineData("集合\r打龙")]
    [InlineData("集合\t打龙")]
    [InlineData("集合\u0000打龙")]
    [InlineData("集合\u2028打龙")]
    [InlineData("集合\u2029打龙")]
    public void TryNormalize_AControlCharacterOrALineSeparator_IsRefused(string input)
    {
        // A chat message is one line, so these are refused rather than flattened into spaces.
        Assert.False(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void TryNormalize_AWellFormedSurrogatePair_IsKept()
    {
        const string input = "集合打龙 🐉";

        Assert.True(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal(input, text);
    }

    [Fact]
    public void TryNormalize_AHighSurrogateAtTheEnd_IsRefused()
    {
        // Cutting the input at a code-unit boundary would send half an emoji, so it is refused instead.
        Assert.False(OverlayVoiceChatText.TryNormalize("集合打龙 \ud83d", out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void TryNormalize_AHighSurrogateFollowedBySomethingElse_IsRefused()
    {
        Assert.False(OverlayVoiceChatText.TryNormalize("集合打龙 \ud83da", out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void TryNormalize_ALowSurrogateOnItsOwn_IsRefused()
    {
        Assert.False(OverlayVoiceChatText.TryNormalize("集合打龙 \udc09", out string text));
        Assert.Equal("", text);
    }

    [Theory]
    [InlineData("/all 集合打龙")]
    [InlineData("/")]
    [InlineData("  /all 集合打龙")]
    public void TryNormalize_TextThatStartsWithASlash_IsRefused(string input)
    {
        // This feature sends one chat message, never a game slash command - and the trim runs first,
        // so a leading space does not hide the slash.
        Assert.False(OverlayVoiceChatText.TryNormalize(input, out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void TryNormalize_ASlashThatIsNotAtTheStart_IsKept()
    {
        Assert.True(OverlayVoiceChatText.TryNormalize("集合/打龙", out string text));
        Assert.Equal("集合/打龙", text);
    }

    // ---- Build ----

    [Fact]
    public void Build_AConfirmedVoiceRequest_SendsTheTrimmedLine()
    {
        IReadOnlyList<string> lines = OverlayVoiceChatText.Build(Voice("  集合打龙  "));

        Assert.Equal("集合打龙", Assert.Single(lines));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("confirmed")]
    [InlineData("target")]
    [InlineData("slot")]
    [InlineData("championId")]
    [InlineData("text")]
    public void Build_ARequestThatFailsOnePrecondition_IsRefused(string fault)
    {
        OverlayChatRequest request = fault switch
        {
            "kind" => Voice("集合打龙", kind: "tier"),
            "confirmed" => Voice("集合打龙", confirmed: false),
            "target" => Voice("集合打龙", target: "own"),
            "slot" => Voice("集合打龙", slot: 1),
            "championId" => Voice("集合打龙", championId: 5),
            _ => Voice(""),
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayVoiceChatText.Build(request)
        );

        Assert.Equal("未发送：请确认一条不超过 180 字符的聊天文字；不支持换行或斜杠命令。", error.Message);
    }

    [Fact]
    public void Build_ASlashCommand_IsRefusedWithTheSameMessage()
    {
        // The same refusal covers both "nothing to send" and "not sendable", so both paths are pinned.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayVoiceChatText.Build(Voice("/all 集合打龙"))
        );

        Assert.Equal("未发送：请确认一条不超过 180 字符的聊天文字；不支持换行或斜杠命令。", error.Message);
    }
}
