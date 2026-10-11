using League.Models;

namespace League.Controls;

internal sealed partial class OpggView
{
    private readonly Button _back = new()
    {
        Text = "←",
        Width = 34,
        Height = 27,
        Enabled = false,
        AccessibleName = "后退",
    };
    private readonly Button _forward = new()
    {
        Text = "→",
        Width = 34,
        Height = 27,
        Enabled = false,
        AccessibleName = "前进",
    };
    private readonly List<OpggQuery> _navigation = new();
    private int _navigationIndex = -1;

    private void RecordNavigation()
    {
        // Preserve the controls' values, including hidden lane/tier selections.
        var page = new OpggQuery(Value(_region), Value(_mode), Value(_tier), Value(_position), _championId);
        if (_navigationIndex >= 0 && _navigation[_navigationIndex] == page)
        {
            return;
        }

        if (_navigationIndex + 1 < _navigation.Count)
        {
            _navigation.RemoveRange(_navigationIndex + 1, _navigation.Count - _navigationIndex - 1);
        }

        _navigation.Add(page);
        if (_navigation.Count > 100)
        {
            _navigation.RemoveAt(0);
        }

        _navigationIndex = _navigation.Count - 1;
        UpdateNavigationButtons();
    }

    private bool MoveNavigation(int direction)
    {
        int next = _navigationIndex + direction;
        if (next < 0 || next >= _navigation.Count)
        {
            return false;
        }

        _filterTimer.Stop();
        Cancel();
        _follow.Checked = false;
        _navigationIndex = next;
        var page = _navigation[next];
        _syncing = true;
        try
        {
            SetValue(_mode, page.Mode);
            SetValue(_region, page.Region);
            SetValue(_tier, page.Tier);
            SetValue(_position, page.Position);
            SelectHero(page.ChampionId);
            _modeTiers[page.Mode] = page.Tier;
            _position.Visible = page.Mode == "ranked";
            _tier.Visible = page.Mode != "arena";
        }
        finally
        {
            _syncing = false;
        }
        UpdateNavigationButtons();
        return true;
    }

    private void UpdateNavigationButtons()
    {
        _back.Enabled = _navigationIndex > 0;
        _forward.Enabled = _navigationIndex + 1 < _navigation.Count;
    }
}
