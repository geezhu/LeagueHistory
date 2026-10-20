using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// Whether a player's profile is public, and the word the widget shows for it.
///
/// The answer has three states, not two. "No profile at all", "a profile that says nothing about privacy" and "a
/// privacy value the client does not use" all mean the same thing to the reader - it is not known - and saying
/// "hidden" for any of them would claim something the data does not support.
/// </summary>
public class PlayerPrivacyTests
{
    private static JObject Profile(string? privacy) =>
        privacy == null ? new JObject() : new JObject { ["privacy"] = privacy };

    // ---- reading the flag ----

    [Fact]
    public void IsPublic_AProfileThatSaysPublic_IsPublic()
    {
        Assert.True(PlayerPrivacy.IsPublic(Profile("PUBLIC")));
    }

    [Fact]
    public void IsPublic_AProfileThatSaysPrivate_IsNot()
    {
        Assert.False(PlayerPrivacy.IsPublic(Profile("PRIVATE")));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("Public")]
    [InlineData("pUbLiC")]
    public void IsPublic_TheWordIsMatchedWhateverItsCase(string privacy)
    {
        // The client's casing is not something to rely on.
        Assert.True(PlayerPrivacy.IsPublic(Profile(privacy)));
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    [InlineData("FRIENDS")]
    public void IsPublic_AnyOtherValue_IsNotKnown(string privacy)
    {
        // A value the client does not use says nothing either way.
        Assert.Null(PlayerPrivacy.IsPublic(Profile(privacy)));
    }

    [Fact]
    public void IsPublic_AProfileWithoutTheField_IsNotKnown()
    {
        Assert.Null(PlayerPrivacy.IsPublic(Profile(null)));
    }

    [Fact]
    public void IsPublic_NoProfileAtAll_IsNotKnown()
    {
        // A player the client has no profile for is not "hidden"; there is simply nothing to say.
        Assert.Null(PlayerPrivacy.IsPublic(null));
    }

    // ---- the word shown ----

    [Theory]
    [InlineData("PUBLIC", "公开")]
    [InlineData("PRIVATE", "隐藏")]
    [InlineData("FRIENDS", "未知")]
    public void Describe_EachOfTheThreeStates_IsOneWord(string privacy, string expected)
    {
        Assert.Equal(expected, PlayerPrivacy.Describe(Profile(privacy)));
    }

    [Fact]
    public void Describe_NoProfile_SaysItIsUnknown()
    {
        Assert.Equal("未知", PlayerPrivacy.Describe(null));
    }
}
