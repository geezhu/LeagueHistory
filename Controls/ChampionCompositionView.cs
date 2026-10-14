using League.Models;
using League.Services;

namespace League.Controls;

/// <summary>
/// Team analysis from arammeta's trained statistics: estimated win rate with its parts, the seven
/// capability totals against their gap thresholds, teammate synergy, and what swapping the user's
/// pick for each bench champion would change. Analysis only; it never picks or swaps champions.
/// </summary>
public sealed class ChampionCompositionView : FlowLayoutPanel
{
    private static readonly Color Muted = Color.FromArgb(150, 172, 182);
    private static readonly Color Body = Color.FromArgb(215, 225, 226);
    private static readonly Color Good = Color.FromArgb(86, 200, 206);
    private static readonly Color Bad = Color.FromArgb(232, 128, 98);
    private static readonly Color Gold = Color.FromArgb(208, 185, 130);
    private static readonly Color Track = Color.FromArgb(22, 52, 64);
    private const int MaxSwaps = 8;

    private readonly Func<int, string> _name;
    private readonly Func<CancellationToken, Task<AramMetaIndex>> _loadIndex;
    private readonly bool _mayhem;
    private readonly CancellationToken _lifetime;
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000 };
    private readonly List<Font> _fonts = new();
    private AramMetaIndex? _index;
    private AramCapabilityGrades? _grades;
    private ChampionSelectRoster? _roster;
    private bool _loading;
    private string? _chatMessage;
    private string? _error;

    public ChampionCompositionView(
        Func<int, string> name,
        Func<CancellationToken, Task<AramMetaIndex>> loadIndex,
        bool mayhem,
        CancellationToken lifetime
    )
    {
        _name = name;
        _loadIndex = loadIndex;
        _mayhem = mayhem;
        _lifetime = lifetime;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(16, 12, 16, 16);
        DoubleBuffered = true;
        SizeChanged += (_, _) => Render();
    }

    /// <summary>The current analysis as one champion select chat line; null until statistics load.</summary>
    public string? ChatMessage => _chatMessage;

    /// <summary>The capability grades as one champion select chat line; null until statistics load.</summary>
    public string? GradesMessage { get; private set; }

    /// <summary>Completes when the current statistics load finishes; used by lifecycle cleanup.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    public void SetRoster(ChampionSelectRoster roster)
    {
        _roster = roster;
        if (_index == null && !_loading)
        {
            Loading = LoadAsync();
        }

        Render();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var index = await _loadIndex(_lifetime);
            // Sampling random teams takes a moment for every team size, so it stays off the UI thread.
            var grades = await Task.Run(() => AramCapabilityGrades.Build(index), _lifetime);
            if (!IsDisposed && !_lifetime.IsCancellationRequested)
            {
                _index = index;
                _grades = grades;
            }
        }
        catch (OperationCanceledException)
        {
            // The roster window closed; nothing is rendered after cancellation.
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Composition statistics: {error}");
            _error = "arammeta 统计暂不可用";
        }
        finally
        {
            _loading = false;
            if (!IsDisposed && !_lifetime.IsCancellationRequested)
            {
                Render();
            }
        }
    }

    private void Render()
    {
        if (IsDisposed || _roster == null)
        {
            return;
        }

        SuspendLayout();
        foreach (Control child in Controls.Cast<Control>().ToArray())
        {
            child.Dispose();
        }

        // Labels from the previous render are gone, so their fonts can be released.
        foreach (var font in _fonts)
        {
            font.Dispose();
        }

        _fonts.Clear();
        _chatMessage = null;
        GradesMessage = null;
        if (_index == null || _grades == null)
        {
            AddStatus();
        }
        else
        {
            int[] team = _roster.Team.Where(entry => entry.ChampionId > 0).Select(entry => entry.ChampionId).ToArray();
            var evaluation = AramTeamScore.Evaluate(_index, team);
            AddSummary(evaluation, _roster.Team.Length);
            AddCapabilities(evaluation);
            if (_mayhem)
            {
                AddPairs(evaluation);
            }

            var swaps = AddSwaps(evaluation);
            _chatMessage = TeamAnalysisChatMessage.Build(
                evaluation,
                swaps.FirstOrDefault(),
                _mayhem,
                _index.Patch,
                _name
            );
            GradesMessage = TeamAnalysisChatMessage.Grades(evaluation, _grades, _mayhem);
        }

        ResumeLayout(true);
    }

    private void AddStatus()
    {
        if (_error == null)
        {
            Add(Line("正在读取 arammeta 统计…", Muted));
            return;
        }

        var retry = Line(_error + " · 点击重试", Good);
        retry.Cursor = Cursors.Hand;
        retry.Click += (_, _) =>
        {
            if (!_loading)
            {
                Loading = LoadAsync();
                Render();
            }
        };
        Add(retry);
    }

    private void AddSummary(AramTeamEvaluation evaluation, int slots)
    {
        int picked = evaluation.Team.Length;
        string scope = picked < slots ? $"已选 {picked}/{slots} 人，按人数折算 · " : "";
        if (_mayhem)
        {
            var title = Line($"估计胜率 {evaluation.EstimatedWinRate:P1}", Gold, 15, FontStyle.Bold);
            Add(title);
            var parts = Line(
                $"英雄 {evaluation.BaseWinRate:P1} · 搭配 {Signed(evaluation.PairLift)} · 阵容 {Signed(evaluation.CompositionScore)} · 可信度 {evaluation.Confidence}",
                Body
            );
            Add(parts);
            _tips.SetToolTip(
                parts,
                "英雄：五人调整后胜率均值；搭配：队友组合的历史提升；阵容：前排、伤害类型与短板的修正。\n"
                    + "可信度按有搭配记录的组合数和样本场次估计。"
            );
            Add(Line($"{scope}arammeta 海克斯大乱斗 {_index!.Patch} 统计模型；不保证单场胜负。", Muted));
        }
        else
        {
            Add(Line("阵容能力", Body, 13, FontStyle.Bold));
            Add(Line($"{scope}能力值参考 arammeta 海克斯大乱斗统计；普通大乱斗不显示估计胜率。", Muted));
        }

        if (evaluation.Missing.Length > 0)
        {
            Add(Line("缺少统计：" + string.Join("、", evaluation.Missing.Select(_name)) + "，未计入能力与胜率。", Bad));
        }
    }

    private void AddCapabilities(AramTeamEvaluation evaluation)
    {
        Add(Heading("能力与短板"));
        foreach (var capability in evaluation.Capabilities)
        {
            Add(CapabilityRow(capability, evaluation.Team.Length));
        }

        string mix = evaluation.AdShare switch
        {
            < 0.25 => "（魔法伤害为主，对手易堆魔抗）",
            > 0.75 => "（物理伤害为主，对手易堆护甲）",
            _ => "",
        };
        Add(
            Line(
                $"物理伤害占比 {evaluation.AdShare:P0}{mix} · 前排 {evaluation.FrontCount} 人",
                mix.Length > 0 ? Bad : Body
            )
        );
    }

    private Control CapabilityRow(AramTeamCapability capability, int size)
    {
        string grade = _grades!.Grade(capability, size);
        Color gradeColor =
            capability.Lack ? Bad
            : grade.StartsWith('S') ? Gold
            : Muted;
        var row = new Panel { Size = new Size(RowWidth, 22), Margin = new Padding(0, 1, 0, 1) };
        // Gap thresholds are low, so a full track is 2.5× the threshold: the tick sits at 40% and the bar
        // shows how far a total clears it. Totals beyond that simply fill the track.
        double scale = capability.Threshold > 0 ? capability.Threshold * 2.5 : Math.Max(1, capability.Sum);
        row.Paint += (_, e) =>
        {
            const int label = 48,
                status = 64;
            var bar = new Rectangle(label, 7, Math.Max(10, row.Width - label - status - 8), 8);
            using var track = new SolidBrush(Track);
            using var fill = new SolidBrush(capability.Lack ? Bad : Good);
            using var tick = new Pen(Muted);
            e.Graphics.FillRectangle(track, bar);
            e.Graphics.FillRectangle(
                fill,
                bar.X,
                bar.Y,
                (int)(bar.Width * Math.Min(1, capability.Sum / scale)),
                bar.Height
            );
            int threshold = bar.X + (int)(bar.Width * Math.Min(1, capability.Threshold / scale));
            e.Graphics.DrawLine(tick, threshold, bar.Y - 3, threshold, bar.Bottom + 3);
            TextRenderer.DrawText(e.Graphics, capability.Name, Font, new Point(0, 3), Body);
            TextRenderer.DrawText(
                e.Graphics,
                $"{grade} {capability.Sum:0.0}",
                Font,
                new Rectangle(bar.Right + 8, 0, status, row.Height),
                gradeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left
            );
        };
        _tips.SetToolTip(
            row,
            $"{capability.Name}：全队合计 {capability.Sum:0.##}，短板阈值 {capability.Threshold:0.##}（竖线）。\n"
                + $"等级 {grade}："
                + (
                    capability.Lack
                        ? "低于短板阈值。"
                        : $"高于 {_grades.Percentile(capability, size):P0} 的同人数随机阵容。"
                )
                + "\n等级只表示与其他阵容相比的位置；估计胜率只计算是否短板。"
        );
        return row;
    }

    private void AddPairs(AramTeamEvaluation evaluation)
    {
        var known = evaluation.Pairs.OrderByDescending(pair => pair.Lift).ToArray();
        Add(Heading($"队友搭配 · 已知 {known.Length}/{evaluation.PairEdges} 组"));
        if (known.Length == 0)
        {
            Add(Line("暂无这些队友组合的历史数据，按 0 计入。", Muted));
            return;
        }

        var shown = known.Take(3).Concat(known.Skip(3).TakeLast(2)).Distinct();
        foreach (var pair in shown)
        {
            var line = Line(
                $"{_name(pair.First)} + {_name(pair.Second)}  {Signed(pair.Lift)}",
                pair.Lift >= 0 ? Good : Bad
            );
            _tips.SetToolTip(line, $"同队 {pair.Games:N0} 场；相对两人各自预期的胜率变化。");
            Add(line);
        }
    }

    private AramTeamSwap[] AddSwaps(AramTeamEvaluation current)
    {
        var self = _roster!.Team.FirstOrDefault(entry => entry.IsSelf);
        if (self == null || self.ChampionId <= 0)
        {
            Add(Heading("换人建议"));
            Add(Line("选择英雄后可比较交换池中的英雄。", Muted));
            return [];
        }

        Add(Heading($"换人建议 · 替换你的 {_name(self.ChampionId)}"));
        if (_roster.Bench.Length == 0)
        {
            Add(Line("交换池暂无英雄。", Muted));
            return [];
        }

        var swaps = AramTeamScore.Swaps(_index!, current, self.ChampionId, _roster.Bench, _mayhem);
        foreach (var swap in swaps.Take(MaxSwaps))
        {
            var parts = new List<string>();
            if (swap.Fixed.Length > 0)
            {
                parts.Add("补" + string.Join("、", swap.Fixed.Select(lack => lack.Name)));
            }

            if (swap.Added.Length > 0)
            {
                parts.Add("新短板 " + string.Join("、", swap.Added.Select(lack => lack.Name)));
            }

            string effect = parts.Count > 0 ? "  · " + string.Join("；", parts) : "";
            string text = _mayhem
                ? $"换成 {_name(swap.ChampionId)}：{Signed(swap.Delta)}{effect}"
                : $"换成 {_name(swap.ChampionId)}{(effect.Length > 0 ? effect : "  · 短板不变")}";
            Add(Line(text, swap.Delta >= 0 && swap.Added.Length == 0 ? Good : Body));
        }

        Add(Line("仅为分析，不会自动选择或交换英雄。", Muted));
        return swaps;
    }

    private int RowWidth => Math.Max(240, Math.Min(560, ClientSize.Width - Padding.Horizontal - 24));

    private Label Heading(string text)
    {
        var label = Line(text, Muted, 9, FontStyle.Bold);
        label.Margin = new Padding(0, 14, 0, 4);
        return label;
    }

    private Label Line(string text, Color color, float size = 9, FontStyle style = FontStyle.Regular)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(RowWidth, 0),
            ForeColor = color,
            Margin = new Padding(0, 2, 0, 2),
        };
        if (size != 9 || style != FontStyle.Regular)
        {
            var font = new Font(Font.FontFamily, size, style);
            _fonts.Add(font);
            label.Font = font;
        }

        return label;
    }

    private void Add(Control control) => Controls.Add(control);

    // Values that round to zero read as neutral rather than "−0.0%".
    private static string Signed(double value) =>
        Math.Abs(value) < 0.0005 ? "±0.0%" : (value > 0 ? "+" : "−") + Math.Abs(value).ToString("P1");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            foreach (var font in _fonts)
            {
                font.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
