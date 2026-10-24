using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The buttons of the end-of-game panel: thumbs-ups after a win, selecting and one send after a loss, and a row
/// counted as sent only once its line reached the chat. The summaries are built directly, three teammates each.
/// </summary>
public class EndOfGamePanelStateTests
{
    [Fact]
    public void Mode_Win_IsHonor()
    {
        var state = new EndOfGamePanelState(Summary(win: true));

        Assert.Equal(EndOfGamePanelMode.Honor, state.Mode);
    }

    [Fact]
    public void Mode_Loss_IsBlock()
    {
        var state = new EndOfGamePanelState(Summary(win: false));

        Assert.Equal(EndOfGamePanelMode.Block, state.Mode);
    }

    [Fact]
    public void Footer_LossWithNothingSelected_Closes()
    {
        var state = new EndOfGamePanelState(Summary(win: false));

        Assert.Equal(EndOfGameFooterAction.Close, state.Footer);
    }

    [Fact]
    public void Footer_LossWithARowSelected_Sends()
    {
        var state = new EndOfGamePanelState(Summary(win: false));

        state.Toggle(1);

        Assert.Equal(EndOfGameFooterAction.Send, state.Footer);
    }

    [Fact]
    public void Footer_LossWithTheSelectionCleared_ClosesAgain()
    {
        var state = new EndOfGamePanelState(Summary(win: false));
        state.Toggle(1);

        state.Toggle(1);

        Assert.Equal(EndOfGameFooterAction.Close, state.Footer);
    }

    [Fact]
    public void Footer_Win_AlwaysCloses()
    {
        var state = new EndOfGamePanelState(Summary(win: true));

        bool toggled = state.Toggle(0);

        Assert.False(toggled);
        Assert.Equal(EndOfGameFooterAction.Close, state.Footer);
    }

    [Fact]
    public void Toggle_UnnamedTeammate_IsRefused()
    {
        var state = new EndOfGamePanelState(Summary(win: false, "甲", "", "丙"));

        bool toggled = state.Toggle(1);

        Assert.False(toggled);
        Assert.Equal(EndOfGameRowState.Idle, state.Row(1));
    }

    [Fact]
    public void Message_LossSelection_NamesTheSelectedInRowOrder()
    {
        var state = new EndOfGamePanelState(Summary(win: false));
        state.Toggle(2);
        state.Toggle(0);

        string message = state.Message(state.Selected);

        Assert.Equal("不怕神一样的对手，就怕甲、丙一样的队友", message);
    }

    [Fact]
    public void Message_WinRow_ThanksThatTeammate()
    {
        var state = new EndOfGamePanelState(Summary(win: true));

        string message = state.Message([1]);

        Assert.Equal("和乙这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory", message);
    }

    [Fact]
    public void Unhonored_AfterOneThumbsUp_LeavesTheOthers()
    {
        var state = new EndOfGamePanelState(Summary(win: true));

        state.MarkSent([0]);

        Assert.Equal([1, 2], state.Unhonored);
    }

    [Fact]
    public void Unhonored_SkipsUnnamedTeammates()
    {
        var state = new EndOfGamePanelState(Summary(win: true, "甲", " ", "丙"));

        Assert.Equal([0, 2], state.Unhonored);
    }

    [Fact]
    public void Unhonored_Loss_IsEmpty()
    {
        var state = new EndOfGamePanelState(Summary(win: false));

        Assert.Empty(state.Unhonored);
    }

    [Fact]
    public void CanAct_SentRow_IsFalse()
    {
        var state = new EndOfGamePanelState(Summary(win: false));
        state.Toggle(0);

        state.MarkSent(state.Selected);

        Assert.False(state.CanAct(0));
        Assert.Equal(EndOfGameRowState.Sent, state.Row(0));
    }

    [Fact]
    public void Selected_RowsNotYetSent_StaySelectedForARetry()
    {
        var state = new EndOfGamePanelState(Summary(win: false));

        state.Toggle(0);

        Assert.Equal([0], state.Selected);
        Assert.Equal(EndOfGameRowState.Selected, state.Row(0));
    }

    [Fact]
    public void MasteryText_LevelAndPoints_ShowsPointsOnly()
    {
        var mastery = new PlayerChampionMastery(1, 12, 186321, null, Available: true);

        string text = EndOfGamePanelState.MasteryText(mastery);

        Assert.Equal("186,321", text);
    }

    [Fact]
    public void MasteryText_Unavailable_IsDashes()
    {
        string text = EndOfGamePanelState.MasteryText(PlayerChampionMastery.Unavailable(1));

        Assert.Equal("--", text);
    }

    private static EndOfGameSummary Summary(bool win, params string[] names) =>
        new(
            1,
            "本人",
            win,
            TimeSpan.FromMinutes(16),
            1,
            new EndOfGameKda(9, 8, 19),
            null,
            [],
            (names.Length == 0 ? ["甲", "乙", "丙"] : names)
                .Select(name => new EndOfGameTeammate(name, 2, new EndOfGameKda(1, 2, 3), null, []))
                .ToArray()
        );
}
