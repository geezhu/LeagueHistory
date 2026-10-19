using League.Models;
using League.Services;

namespace League.Controls;

/// <summary>
/// Edits the champions that chime when they are on the enemy team. Champion names come from the client, so until it
/// is connected the saved champions show by id and nothing can be added.
/// </summary>
internal sealed class ChampionAlertSettingsController
{
    private readonly Func<IReadOnlyList<Champion>> _champions;
    private readonly Func<IReadOnlyList<int>> _saved;
    private readonly Action<IReadOnlyList<int>> _save;
    private readonly Action _preview;
    private readonly ComboBox _search = new()
    {
        Name = "ChampionAlertSearch",
        AccessibleName = "要提醒的英雄",
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems,
        Width = 180,
    };
    private readonly Button _add = new() { Name = "ChampionAlertAdd", Text = "添加" };
    private readonly Button _test = new() { Name = "ChampionAlertPreview", Text = "试听" };
    private readonly Label _status = new() { Name = "ChampionAlertStatus" };
    private readonly FlowLayoutPanel _chips = new()
    {
        Name = "ChampionAlertList",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        MaximumSize = new Size(420, 0),
    };

    public ChampionAlertSettingsController(
        Func<IReadOnlyList<Champion>> champions,
        Func<IReadOnlyList<int>> saved,
        Action<IReadOnlyList<int>> save,
        Action preview
    )
    {
        _champions = champions;
        _saved = saved;
        _save = save;
        _preview = preview;
        _add.Click += (_, _) => AddTyped();
        _test.Click += (_, _) => _preview();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddTyped();
            }
        };
        // The client may connect after the settings page is built; names and suggestions are read when shown.
        _search.Enter += (_, _) => LoadSuggestions();
        _chips.VisibleChanged += (_, _) => ShowSaved();
    }

    public Control[] Content
    {
        get
        {
            var add = new SettingRow("添加英雄", "游戏开始时敌方阵容里有这些英雄，就播放提示音", _search, _add, _test);
            add.AddNote(_status);
            var list = new SettingRow("提醒列表", "点击英雄即可移除");
            list.AddNote(_chips);
            return [add, list];
        }
    }

    public void Attach()
    {
        LoadSuggestions();
        ShowSaved();
    }

    private void LoadSuggestions()
    {
        var champions = _champions();
        if (champions.Count == _search.Items.Count)
        {
            return;
        }

        _search.BeginUpdate();
        _search.Items.Clear();
        _search.Items.AddRange(
            champions
                .Where(c => c.Id > 0)
                .OrderBy(c => c.Name, StringComparer.CurrentCulture)
                .Select(c => (object)ChampionNameSearch.Label(c))
                .ToArray()
        );
        _search.EndUpdate();
    }

    private void AddTyped()
    {
        var champions = _champions();
        if (champions.Count == 0)
        {
            _status.Text = "请先启动并登录英雄联盟客户端，读取英雄列表后再添加。";
            return;
        }

        var matches = ChampionNameSearch.Search(_search.Text, champions);
        if (matches.Count != 1)
        {
            _status.Text =
                matches.Count == 0
                    ? "没有找到这个英雄，可以输入名字、称号、英文名或拼音首字母。"
                    : $"匹配到 {matches.Count} 个英雄：{string.Join("、", matches.Take(5).Select(c => c.Name))}{(matches.Count > 5 ? " 等" : "")}，请输入完整名字或从下拉列表选择。";
            return;
        }

        var champion = matches[0];
        var saved = _saved();
        if (saved.Contains(champion.Id))
        {
            _status.Text = $"{champion.Name} 已在提醒列表中。";
            return;
        }

        _save([.. saved, champion.Id]);
        _search.Text = "";
        _status.Text = $"已添加 {champion.Name}。";
        ShowSaved();
    }

    private void Remove(int id)
    {
        _save(_saved().Where(saved => saved != id).ToArray());
        _status.Text = "";
        ShowSaved();
    }

    private void ShowSaved()
    {
        var names = _champions().ToDictionary(c => c.Id, c => c.Name);
        var saved = _saved();
        _chips.SuspendLayout();
        foreach (Control chip in _chips.Controls.Cast<Control>().ToArray())
        {
            chip.Dispose();
        }

        if (saved.Count == 0)
        {
            _chips.Controls.Add(new Label { Text = "还没有添加英雄，提醒处于关闭状态。", AutoSize = true });
        }

        foreach (int id in saved)
        {
            var chip = new Button
            {
                Text = (names.TryGetValue(id, out var name) ? name : $"英雄 #{id}") + "  ×",
                AccessibleName = "移除提醒",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 6, 6),
            };
            chip.FlatAppearance.BorderColor = Color.FromArgb(205, 216, 226);
            chip.Click += (_, _) => Remove(id);
            _chips.Controls.Add(chip);
        }

        _chips.ResumeLayout();
    }
}
