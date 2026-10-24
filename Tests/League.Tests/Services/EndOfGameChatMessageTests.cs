using League.Services;

namespace League.Tests.Services;

/// <summary>The two lines of the end-of-game panel: one name or several joined with 、, never a broken line.</summary>
public class EndOfGameChatMessageTests
{
    [Fact]
    public void Honor_OneName_ThanksThatPlayer()
    {
        string message = EndOfGameChatMessage.Honor(["混学家"]);

        Assert.Equal("和混学家这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory", message);
    }

    [Fact]
    public void Honor_SeveralNames_JoinsThemInOneLine()
    {
        string message = EndOfGameChatMessage.Honor(["甲", "乙", "丙"]);

        Assert.Equal("和甲、乙、丙这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory", message);
    }

    [Fact]
    public void Block_OneName_UsesTheLossLine()
    {
        string message = EndOfGameChatMessage.Block(["杀神33"]);

        Assert.Equal("不怕神一样的对手，就怕杀神33一样的队友", message);
    }

    [Fact]
    public void Block_SeveralNames_JoinsThemInOneLine()
    {
        string message = EndOfGameChatMessage.Block(["甲", "乙"]);

        Assert.Equal("不怕神一样的对手，就怕甲、乙一样的队友", message);
    }

    [Fact]
    public void Honor_NameWithLineBreak_StaysOnOneLine()
    {
        string message = EndOfGameChatMessage.Honor([" 甲\r\n乙 "]);

        Assert.Equal("和甲  乙这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory", message);
    }

    [Fact]
    public void Honor_BlankNamesOnly_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => EndOfGameChatMessage.Honor(["", "  "]));
    }
}
