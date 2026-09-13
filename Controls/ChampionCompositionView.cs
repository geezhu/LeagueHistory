using League.Clients;
using League.Models;
using System.Text;

namespace League.Controls;

public sealed partial class ChampionCompositionView : UserControl
{
    private readonly ChampionAnalysisClient _client;
    private readonly Func<int, (string Name, string Alias)> _champion;
    private readonly Func<TimeSpan> _lifetime;
    private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 78, Padding = new Padding(10, 6, 10, 3) };
    private readonly DarkFilterComboBox _replacement = new() { Width = 190 };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Top, Height = 196, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        BackgroundColor = Color.FromArgb(8, 30, 40), BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, EnableHeadersVisualStyles = false };
    private readonly RichTextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false };
    private readonly Dictionary<int, ChampionAnalysisSnapshot> _snapshots = new();
    private readonly Dictionary<int, string> _errors = new();
    private ChampionSelectRoster? _roster;
    private CancellationTokenSource? _request;
    private bool _changing;
    private sealed record Choice(int Id, string Name) { public override string ToString() => Name; }

    public ChampionCompositionView(Func<int, (string Name, string Alias)> champion, Func<TimeSpan> lifetime,
        ChampionAnalysisClient? client = null)
    {
        _champion = champion; _lifetime = lifetime; _client = client ?? ChampionAnalysisClient.Shared;
        BackColor = Color.FromArgb(8, 30, 40); ForeColor = Color.FromArgb(202, 222, 228);
        Padding = new Padding(8);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        bar.Controls.Add(new Label { Text = "替换自己预览", AutoSize = true, Margin = new Padding(2, 8, 8, 0) });
        bar.Controls.Add(_replacement);
        var retry = new Button { Text = "重新读取", AutoSize = true, FlatStyle = FlatStyle.Flat };
        bar.Controls.Add(retry);
        _grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = BackColor, ForeColor = ForeColor,
            SelectionBackColor = Color.FromArgb(23, 76, 89), SelectionForeColor = Color.White, Padding = new Padding(4) };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(16, 45, 57), ForeColor = ForeColor };
        _grid.GridColor = Color.FromArgb(30, 58, 68); _grid.RowTemplate.Height = 30;
        foreach (string name in new[] { "英雄", "生命", "护甲 / 魔抗", "普攻距离", "普攻周期", "数据" })
            _grid.Columns.Add(name, name);
        _grid.Columns[0].FillWeight = 130;
        _details.BackColor = BackColor; _details.ForeColor = ForeColor; _details.Font = Font;
        InitializeDashboard(bar);
        _replacement.SelectedIndexChanged += (_, _) => { if (!_changing) { Render(); _ = LoadAsync(); } };
        _grid.SelectionChanged += (_, _) => ShowDetails();
        retry.Click += (_, _) => _ = LoadAsync();
    }

    public void SetRoster(ChampionSelectRoster roster)
    {
        bool changed = _roster?.Signature != roster.Signature;
        _roster = roster;
        if (!changed) return;
        int previous = (_replacement.SelectedItem as Choice)?.Id ?? 0;
        _changing = true;
        _replacement.Items.Clear(); _replacement.Items.Add(new Choice(0, "保持当前阵容"));
        foreach (int id in roster.Bench) _replacement.Items.Add(new Choice(id, _champion(id).Name));
        _replacement.SelectedIndex = Math.Max(0, _replacement.Items.Cast<Choice>().ToList().FindIndex(c => c.Id == previous));
        _replacement.Enabled = roster.Team.Any(e => e.IsSelf);
        _changing = false;
        Render(); _ = LoadAsync();
    }

    private ChampionSelectRosterEntry[] Team()
    {
        int id = (_replacement.SelectedItem as Choice)?.Id ?? 0;
        return _roster?.Team.Select(e => e.IsSelf && id > 0 ? e with { ChampionId = id, Preview = true } : e).ToArray() ?? [];
    }

    private async Task LoadAsync()
    {
        _request?.Cancel(); _request?.Dispose();
        _request = new CancellationTokenSource(); var token = _request.Token;
        // Keep the actual self for comparison; never download the entire bench.
        var ids = Team().Concat(_roster?.Team.Where(e => e.IsSelf) ?? []).Where(e => e.ChampionId > 0).Select(e => e.ChampionId).Distinct().ToArray();
        using var concurrency = new SemaphoreSlim(3);
        await Task.WhenAll(ids.Select(async id =>
        {
            bool entered = false;
            try
            {
                await concurrency.WaitAsync(token); entered = true;
                var champion = _champion(id);
                if (string.IsNullOrWhiteSpace(champion.Alias)) throw new InvalidDataException("本地英雄别名尚未加载。");
                var result = await _client.GetAsync(champion.Alias, _lifetime(), token);
                if (token.IsCancellationRequested || IsDisposed) return;
                _snapshots[id] = result; _errors.Remove(id);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || IsDisposed) return;
                _errors[id] = ex.Message;
            }
            finally { if (entered) concurrency.Release(); }
            if (!token.IsCancellationRequested && !IsDisposed) Render();
        }));
    }

    private static double? Value(ChampionAnalysisReport report, string name) => report.Attributes.FirstOrDefault(a => a.Name == name)?.Values.FirstOrDefault();
    private static string Number(double? value, string format = "0.#") => value?.ToString(format) ?? "—";
    private void Render()
    {
        int? selected = _grid.CurrentRow?.Tag as int?;
        var team = Team();
        _grid.Rows.Clear();
        foreach (var entry in team)
        {
            int id = entry.ChampionId;
            _snapshots.TryGetValue(id, out var snapshot);
            var r = snapshot?.Report;
            string data = _errors.ContainsKey(id) ? "读取失败" : snapshot == null ? (id > 0 ? "读取中" : "未选择") : snapshot.Stale ? "旧缓存" : "已读取";
            string name = id > 0 ? _champion(id).Name : "等待选择";
            var row = _grid.Rows[_grid.Rows.Add(name + (entry.IsSelf ? " · 自己" : "") + (entry.Preview ? "（预选）" : ""),
                r == null ? "—" : Number(Value(r, "基础生命")),
                r == null ? "—" : Number(Value(r, "基础护甲")) + " / " + Number(Value(r, "基础魔抗")),
                r == null ? "—" : Number(Value(r, "普攻距离")),
                r == null ? "—" : Number(Value(r, "基础普攻周期（不含前摇、重置）"), "0.00") + " 秒", data)];
            row.Tag = id;
        }
        if (_grid.Rows.Count > 0)
        {
            var row = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (int?)r.Tag == selected) ?? _grid.Rows[0];
            _grid.CurrentCell = row.Cells[0];
        }
        int known = team.Count(e => _snapshots.ContainsKey(e.ChampionId));
        _summary.Text = $"阵容分析 · 已读取 {known}/{team.Length} 人\n基础值，不含成长、装备、符文、强化与地图平衡。换人预览不会操作客户端。";
        int replacement = (_replacement.SelectedItem as Choice)?.Id ?? 0;
        int original = _roster?.Team.FirstOrDefault(e => e.IsSelf)?.ChampionId ?? 0;
        if (replacement > 0 && _snapshots.TryGetValue(original, out var before) && _snapshots.TryGetValue(replacement, out var after))
        {
            string Delta(string field) => (Value(after.Report, field) - Value(before.Report, field))?.ToString("+0.#;-0.#;0") ?? "未知";
            _summary.Text += $"\n相对 {_champion(original).Name}：生命 {Delta("基础生命")} · 护甲 {Delta("基础护甲")} · 魔抗 {Delta("基础魔抗")} · 普攻距离 {Delta("普攻距离")}";
        }
        RenderDashboard();
        ShowDetails();
    }

    private void ShowDetails()
    {
        int id = _grid.CurrentRow?.Tag as int? ?? 0;
        if (!_snapshots.TryGetValue(id, out var snapshot))
        { _details.Text = _errors.GetValueOrDefault(id) ?? (id > 0 ? "正在读取英雄数据…" : "选择一名英雄查看详细数据。"); return; }
        var text = new StringBuilder();
        text.AppendLine($"{_champion(id).Name} · 基础能力\n");
        foreach (var value in snapshot.Report.Attributes)
            text.AppendLine($"{value.Name}：{Number(value.Values.FirstOrDefault(), "0.###")} {value.Unit}");
        text.AppendLine("\n技能分析\n控制类型、实际射程、命中条件和护盾数值仍在验证中，当前不计入阵容合计。");
        foreach (var spell in snapshot.Report.Spells)
        {
            text.AppendLine($"\n{spell.Key} · 目标类型 {spell.Targeting}（不代表必中）");
            foreach (var formula in spell.Formulas.Where(f => f.Name.Contains("Shield", StringComparison.OrdinalIgnoreCase)))
                text.AppendLine("护盾公式待验证：" + formula.Expression);
            if (spell.Warnings.Any(w => w.Contains("ChainDuration"))) text.AppendLine("连接阶段时间不计入已确认控制时长。");
        }
        text.AppendLine($"\n来源：CommunityDragon · latest（未锁定补丁）\n读取时间：{snapshot.FetchedAt.LocalDateTime:yyyy-MM-dd HH:mm}\n快照：{snapshot.ContentHash[..12]}");
        if (snapshot.Warning != null) text.AppendLine(snapshot.Warning);
        _details.Text = text.ToString();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { DisposeDashboard(); _request?.Cancel(); _request?.Dispose(); _request = null; }
        base.Dispose(disposing);
    }
}
