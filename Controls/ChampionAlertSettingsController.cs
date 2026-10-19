using League.Models;

namespace League.Controls;

/// <summary>
/// The settings row for champion alerts: which champions are watched, a link to the picker window and a sound preview.
/// Names come from the client, so until it is connected the saved champions show by id.
/// </summary>
internal sealed class ChampionAlertSettingsController
{
    private readonly Func<IReadOnlyList<Champion>> _champions;
    private readonly Func<IReadOnlyList<int>> _saved;
    private readonly LinkLabel _open = new() { Name = "ChampionAlertOpen", Text = "配置提醒英雄 ›" };
    private readonly Button _test = new() { Name = "ChampionAlertPreview", Text = "试听" };
    private readonly Label _summary = new() { Name = "ChampionAlertSummary" };

    public ChampionAlertSettingsController(
        Func<IReadOnlyList<Champion>> champions,
        Func<IReadOnlyList<int>> saved,
        Action open,
        Action preview
    )
    {
        _champions = champions;
        _saved = saved;
        _open.LinkClicked += (_, _) =>
        {
            open();
            ShowSaved();
        };
        _test.Click += (_, _) => preview();
        // The client may connect after the settings page is built; names are read again when the page is shown.
        _summary.VisibleChanged += (_, _) => ShowSaved();
    }

    public Control[] Content
    {
        get
        {
            var row = new SettingRow("提醒英雄", "游戏开始时敌方阵容里有这些英雄，就播放提示音", _open, _test);
            row.AddNote(_summary);
            return [row];
        }
    }

    public void Attach() => ShowSaved();

    public void ShowSaved()
    {
        var saved = _saved();
        if (saved.Count == 0)
        {
            _summary.Text = "还没有添加英雄，提醒处于关闭状态。";
            return;
        }

        var names = _champions().GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name);
        _summary.Text =
            $"已添加 {saved.Count} 个："
            + string.Join("、", saved.Select(id => names.TryGetValue(id, out var name) ? name : $"英雄 #{id}"));
    }
}
