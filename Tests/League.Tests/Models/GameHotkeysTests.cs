using League.Models;

namespace League.Tests.Models;

public class GameHotkeysTests
{
    [Theory]
    [InlineData("F9", Keys.F9)]
    [InlineData("Ctrl+F7", Keys.Control | Keys.F7)]
    [InlineData("ctrl + f7", Keys.Control | Keys.F7)]
    [InlineData("Control+F7", Keys.Control | Keys.F7)]
    [InlineData("Alt+Shift+Q", Keys.Alt | Keys.Shift | Keys.Q)]
    [InlineData("Shift+Ctrl+D1", Keys.Control | Keys.Shift | Keys.D1)]
    public void Parse_AWrittenShortcut_ReadsTheKeys(string text, Keys expected)
    {
        Keys keys = GameHotkeys.Parse(text, Keys.F12);

        Assert.Equal(expected, keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Shift+A")]
    [InlineData("A")]
    [InlineData("Ctrl+ControlKey")]
    public void Parse_AnUnusableShortcut_GivesTheFallback(string? text)
    {
        Keys keys = GameHotkeys.Parse(text, Keys.F9);

        Assert.Equal(Keys.F9, keys);
    }

    [Theory]
    [InlineData(Keys.F9, "F9")]
    [InlineData(Keys.Control | Keys.F7, "Ctrl+F7")]
    [InlineData(Keys.Shift | Keys.Alt | Keys.Control | Keys.F7, "Ctrl+Alt+Shift+F7")]
    public void Format_Keys_WritesModifiersInAFixedOrder(Keys keys, string expected)
    {
        Assert.Equal(expected, GameHotkeys.Format(keys));
    }

    [Theory]
    [InlineData(Keys.F1)]
    [InlineData(Keys.F24)]
    [InlineData(Keys.Shift | Keys.F5)]
    [InlineData(Keys.Control | Keys.A)]
    [InlineData(Keys.Alt | Keys.D3)]
    public void IsAllowed_AFunctionKeyOrACtrlOrAltCombination_IsAllowed(Keys keys)
    {
        Assert.True(GameHotkeys.IsAllowed(keys));
    }

    [Theory]
    [InlineData(Keys.A)]
    [InlineData(Keys.Shift | Keys.A)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Control)]
    [InlineData(Keys.Control | Keys.ControlKey)]
    public void IsAllowed_ABareKeyOrAModifierAlone_IsRefused(Keys keys)
    {
        Assert.False(GameHotkeys.IsAllowed(keys));
    }

    [Fact]
    public void From_ADefaultConfig_GivesTheDocumentedShortcuts()
    {
        var shortcuts = GameHotkeys.From(new LeagueConfig());

        Assert.Equal(GameHotkeys.Defaults, shortcuts);
    }

    [Fact]
    public void From_AnUnusableSetting_FallsBackToThatActionsDefault()
    {
        var config = new LeagueConfig { HotkeyMyTeam = "A" };

        var shortcuts = GameHotkeys.From(config);

        Assert.Equal(Keys.F9, shortcuts[GameHotkeyAction.MyTeam]);
    }

    [Fact]
    public void Store_AShortcut_WritesItInTheSettingsSpelling()
    {
        var config = new LeagueConfig();

        GameHotkeys.Store(config, GameHotkeyAction.FullTeam, Keys.Control | Keys.F10);

        Assert.Equal("Ctrl+F10", config.HotkeyFullTeam);
    }

    [Fact]
    public void Parse_TheStoredSpelling_ReadsBackTheSameKeys()
    {
        var keys = Keys.Control | Keys.Alt | Keys.F6;

        Keys roundTrip = GameHotkeys.Parse(GameHotkeys.Format(keys), Keys.F9);

        Assert.Equal(keys, roundTrip);
    }
}
