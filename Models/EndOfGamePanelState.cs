namespace League.Models;

/// <summary>A win thanks teammates, a loss names the ones to blame.</summary>
public enum EndOfGamePanelMode
{
    Honor,
    Block,
}

public enum EndOfGameRowState
{
    Idle,

    /// <summary>Picked for the next block line; only a loss selects.</summary>
    Selected,

    /// <summary>Already named in a line that reached the chat.</summary>
    Sent,
}

public enum EndOfGameFooterAction
{
    Close,
    Send,
}

/// <summary>
/// What the end-of-game panel's buttons do. After a win each row sends its own thumbs-up and "honor all" covers the
/// rows not yet sent. After a loss rows are only selected, and the footer turns from close into send while any is.
/// A row is marked sent only once its line reached the chat, so a failed send can be retried.
/// </summary>
public sealed class EndOfGamePanelState
{
    private readonly EndOfGameRowState[] _rows;

    public EndOfGamePanelState(EndOfGameSummary summary)
    {
        Summary = summary;
        Mode = summary.Win ? EndOfGamePanelMode.Honor : EndOfGamePanelMode.Block;
        _rows = new EndOfGameRowState[summary.Teammates.Count];
    }

    public EndOfGameSummary Summary { get; }

    public EndOfGamePanelMode Mode { get; }

    public EndOfGameRowState Row(int index) => _rows[index];

    public bool CanAct(int index) => Summary.Teammates[index].CanBeNamed && _rows[index] != EndOfGameRowState.Sent;

    public IReadOnlyList<int> Selected => Indices(EndOfGameRowState.Selected);

    /// <summary>The rows "honor all" still covers; empty after a loss.</summary>
    public IReadOnlyList<int> Unhonored =>
        Mode == EndOfGamePanelMode.Honor
            ? Enumerable.Range(0, _rows.Length).Where(CanAct).ToArray()
            : Array.Empty<int>();

    public EndOfGameFooterAction Footer =>
        Mode == EndOfGamePanelMode.Block && Selected.Count > 0
            ? EndOfGameFooterAction.Send
            : EndOfGameFooterAction.Close;

    /// <summary>Selects or clears a row for the block line; false when the row cannot be picked.</summary>
    public bool Toggle(int index)
    {
        if (Mode != EndOfGamePanelMode.Block || !CanAct(index))
        {
            return false;
        }

        _rows[index] = _rows[index] == EndOfGameRowState.Selected ? EndOfGameRowState.Idle : EndOfGameRowState.Selected;
        return true;
    }

    /// <summary>The one chat line naming these rows, in the wording of the panel's mode.</summary>
    public string Message(IReadOnlyList<int> rows)
    {
        var names = rows.Select(index => Summary.Teammates[index].Name);
        return Mode == EndOfGamePanelMode.Honor
            ? Services.EndOfGameChatMessage.Honor(names)
            : Services.EndOfGameChatMessage.Block(names);
    }

    public void MarkSent(IReadOnlyList<int> rows)
    {
        foreach (int index in rows)
        {
            _rows[index] = EndOfGameRowState.Sent;
        }
    }

    /// <summary>The mastery cell of the header: the points of the champion played, "--" when unknown.</summary>
    public static string MasteryText(PlayerChampionMastery? mastery) =>
        mastery is { Available: true, Points: long points } ? $"{points:N0}" : "--";

    private int[] Indices(EndOfGameRowState state) =>
        Enumerable.Range(0, _rows.Length).Where(index => _rows[index] == state).ToArray();
}
