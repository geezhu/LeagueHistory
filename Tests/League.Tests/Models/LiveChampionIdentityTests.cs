using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Working out which champion a live player is, from the two names the game gives.
///
/// The alias comes out of the raw resource name, which is written one of two ways: a legacy display name, or a
/// "Character_..._Name" pair. Whatever comes out is only trusted if it is a plausible identifier - up to eighty
/// characters of letters, digits and underscores - because the same field sometimes carries a localised name instead,
/// and matching a localised name against aliases would pick the wrong champion.
///
/// Resolution is all-or-nothing. Two champions sharing a name is a real case (Azir and a summon, or two entries in a
/// stale catalog), and picking one of them would silently show the wrong champion; so two matches resolve to nothing.
/// </summary>
public class LiveChampionIdentityTests
{
    private static LiveMatchPlayer Player(string alias = "", string name = "") =>
        new("p", "名字", alias, name, "ORDER", Self: false, Level: 11, Items: []);

    private static Champion Champion(int id, string alias, string? name = null) =>
        new()
        {
            Id = id,
            Alias = alias,
            Name = name ?? "名字" + id,
            Description = "",
        };

    // ---- reading the alias ----

    [Theory]
    [InlineData("Character_Ahri_Name", "Ahri")]
    [InlineData("character_ahri_name", "ahri")]
    [InlineData("CHARACTER_AHRI_NAME", "AHRI")]
    public void ReadAlias_TheCharacterForm_IsUnwrapped(string raw, string expected)
    {
        Assert.Equal(expected, LiveChampionIdentity.ReadAlias(raw));
    }

    [Theory]
    [InlineData("game_character_displayname_Ahri", "Ahri")]
    [InlineData("GAME_CHARACTER_DISPLAYNAME_Ahri", "Ahri")]
    public void ReadAlias_TheLegacyForm_IsUnwrapped(string raw, string expected)
    {
        Assert.Equal(expected, LiveChampionIdentity.ReadAlias(raw));
    }

    [Fact]
    public void ReadAlias_Padding_IsTrimmedFirst()
    {
        Assert.Equal("Ahri", LiveChampionIdentity.ReadAlias("  Character_Ahri_Name  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ahri")]
    [InlineData("Character_Ahri")]
    [InlineData("Ahri_Name")]
    public void ReadAlias_SomethingThatIsNotOneOfTheForms_IsNothing(string? raw)
    {
        Assert.Equal("", LiveChampionIdentity.ReadAlias(raw));
    }

    [Fact]
    public void ReadAlias_APrefixAndSuffixWithNothingBetween_IsNothing()
    {
        // "Character__Name" is the two markers and no name at all.
        Assert.Equal("", LiveChampionIdentity.ReadAlias("Character__Name"));
    }

    [Theory]
    [InlineData("Character_Ah-ri_Name")]
    [InlineData("Character_九尾_Name")]
    [InlineData("Character_Ah ri_Name")]
    public void ReadAlias_ANameThatIsNotAnIdentifier_IsNothing(string raw)
    {
        // The same field carries localised names sometimes; matching those against aliases would pick wrongly.
        Assert.Equal("", LiveChampionIdentity.ReadAlias(raw));
    }

    [Fact]
    public void ReadAlias_EightyCharacters_IsStillAccepted()
    {
        // The bound exists to reject a slot that carries a sentence, not to reject long identifiers.
        string alias = new('a', 80);

        Assert.Equal(alias, LiveChampionIdentity.ReadAlias("Character_" + alias + "_Name"));
    }

    [Fact]
    public void ReadAlias_EightyOneCharacters_IsRefused()
    {
        string alias = new('a', 81);

        Assert.Equal("", LiveChampionIdentity.ReadAlias("Character_" + alias + "_Name"));
    }

    [Fact]
    public void ReadAlias_UnderscoresInsideTheName_AreAllowed()
    {
        Assert.Equal("Ah_ri", LiveChampionIdentity.ReadAlias("Character_Ah_ri_Name"));
    }

    // ---- resolving the champion ----

    [Fact]
    public void Resolve_TheAliasMatchesChampion_IsThatOne()
    {
        Champion[] catalog = [Champion(1, "Ahri"), Champion(2, "Zed")];

        Assert.Equal(2, LiveChampionIdentity.Resolve(Player(alias: "Character_Zed_Name"), catalog)?.Id);
    }

    [Fact]
    public void Resolve_TheAliasIsMatchedWhateverItsCase()
    {
        Champion[] catalog = [Champion(1, "Ahri")];

        Assert.Equal(1, LiveChampionIdentity.Resolve(Player(alias: "Character_AHRI_Name"), catalog)?.Id);
    }

    [Fact]
    public void Resolve_TwoChampionsWithTheSameAlias_ResolveToNothing()
    {
        // Picking one of them would silently show the wrong champion.
        Champion[] catalog = [Champion(1, "Ahri"), Champion(2, "ahri")];

        Assert.Null(LiveChampionIdentity.Resolve(Player(alias: "Character_Ahri_Name"), catalog));
    }

    [Fact]
    public void Resolve_NothingMatchesTheAlias_IsNothing()
    {
        Assert.Null(LiveChampionIdentity.Resolve(Player(alias: "Character_Nobody_Name"), [Champion(1, "Ahri")]));
    }

    [Fact]
    public void Resolve_WithoutAnAlias_TheChampionNameIsUsed()
    {
        // When the game omits the raw key, the resource name still identifies the champion.
        Champion[] catalog = [Champion(1, "Ahri", "九尾妖狐")];

        Assert.Equal(1, LiveChampionIdentity.Resolve(Player(name: "九尾妖狐"), catalog)?.Id);
    }

    [Fact]
    public void Resolve_WithoutAnAlias_TheAliasFieldIsTriedAsAName()
    {
        // A raw field that is already a bare alias falls through to the second pass.
        Champion[] catalog = [Champion(1, "Ahri")];

        Assert.Equal(1, LiveChampionIdentity.Resolve(Player(alias: "Ahri"), catalog)?.Id);
    }

    [Fact]
    public void Resolve_AChampionWithNoId_IsNotACandidate()
    {
        // A zero id is a placeholder, not a champion that can be shown.
        Champion[] catalog = [Champion(0, "Ahri")];

        Assert.Null(LiveChampionIdentity.Resolve(Player(alias: "Character_Ahri_Name"), catalog));
    }

    [Fact]
    public void Resolve_TwoChampionsWithTheSameName_ResolveToNothing()
    {
        Champion[] catalog = [Champion(1, "A", "九尾妖狐"), Champion(2, "B", "九尾妖狐")];

        Assert.Null(LiveChampionIdentity.Resolve(Player(name: "九尾妖狐"), catalog));
    }

    [Fact]
    public void Resolve_AnEmptyCatalog_IsNothing()
    {
        Assert.Null(LiveChampionIdentity.Resolve(Player(alias: "Character_Ahri_Name", name: "九尾妖狐"), []));
    }

    [Fact]
    public void Resolve_NothingToGoOn_IsNothing()
    {
        // A seat with no name at all cannot be identified.
        Assert.Null(LiveChampionIdentity.Resolve(Player(), [Champion(1, "Ahri")]));
    }
}
