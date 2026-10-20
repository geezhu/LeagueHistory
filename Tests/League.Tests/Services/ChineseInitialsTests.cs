using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Pinyin initials for the Game Bar search, which keeps its input method in English.
///
/// The mapping is not a table of names but a property of GB2312: the level-1 characters are ordered by pinyin, so each
/// initial is a code range. That makes the behaviour easy to state and easy to get subtly wrong - the level-2
/// characters are ordered by radical instead, so they are left out rather than mapped to whatever range they land in,
/// and a character outside the encoded range produces nothing at all.
///
/// Two characters are pinned by name ("阿" is the first code of the 'a' range and "八" the first of 'b'); the rest is
/// stated as properties, so an encoding table change would be visible rather than silently accepted.
/// </summary>
public class ChineseInitialsTests
{
    // ---- nothing to render ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Of_Nothing_IsEmpty(string? text)
    {
        Assert.Equal("", ChineseInitials.Of(text));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("！")]
    [InlineData("-")]
    [InlineData("#")]
    public void Of_Punctuation_IsLeftOut(string text)
    {
        // Only letters, digits and level-1 Chinese render; a search key with punctuation in it would never match.
        Assert.Equal("", ChineseInitials.Of(text));
    }

    // ---- what is kept ----

    [Fact]
    public void Of_LettersAndDigits_AreKeptInLowerCase()
    {
        Assert.Equal("abc123", ChineseInitials.Of("aBc123"));
    }

    // ---- the two pinned characters ----

    [Fact]
    public void Of_TheFirstPinyinCharacter_IsA()
    {
        // 阿 is the first code of the 'a' range, so it is also the anchor for the whole table.
        Assert.Equal("a", ChineseInitials.Of("阿"));
    }

    [Fact]
    public void Of_TheFirstBCharacter_IsB()
    {
        Assert.Equal("b", ChineseInitials.Of("八"));
    }

    [Fact]
    public void Of_TheTwoTogether_AreInOrder()
    {
        Assert.Equal("ab", ChineseInitials.Of("阿八"));
    }

    // ---- the properties the mapping rests on ----

    [Fact]
    public void Of_AChineseName_IsOneLetterPerCharacter()
    {
        // Four characters in, four letters out - not five, and not zero.
        string initials = ChineseInitials.Of("坦克引擎");

        Assert.Equal(4, initials.Length);
        Assert.All(initials, c => Assert.True(char.IsAsciiLetterLower(c), $"'{c}' is not a lower-case letter."));
    }

    [Theory]
    [InlineData("阿")]
    [InlineData("八")]
    [InlineData("亚")]
    [InlineData("索")]
    [InlineData("坦")]
    [InlineData("克")]
    [InlineData("座")]
    [InlineData("做")]
    public void Of_OneChineseCharacter_IsOneLowerLetterOrNothing(string text)
    {
        // Whatever a character maps to, it maps to a single letter or to nothing - never to a string.
        string initial = ChineseInitials.Of(text);

        Assert.True(initial.Length <= 1, $"'{text}' produced '{initial}'.");
        Assert.True(
            initial.Length == 0 || char.IsAsciiLetterLower(initial[0]),
            $"'{initial}' is not a lower-case letter."
        );
    }

    [Fact]
    public void Of_TheWhole_IsThePartsJoined()
    {
        // The mapping is per character, so rendering a name is rendering its characters in order.
        const string name = "亚索阿八";

        string joined = string.Concat(name.Select(c => ChineseInitials.Of(c.ToString())));

        Assert.Equal(joined, ChineseInitials.Of(name));
    }

    [Fact]
    public void Of_ALetterBetweenChineseCharacters_KeepsItsPlace()
    {
        // A digit or letter in a name is not dropped, and it does not disturb the neighbours.
        Assert.Equal("a1b", ChineseInitials.Of("阿1八"));
    }

    [Fact]
    public void Of_RepeatedCharacters_RepeatTheirInitials()
    {
        Assert.Equal("aa", ChineseInitials.Of("阿阿"));
    }
}
