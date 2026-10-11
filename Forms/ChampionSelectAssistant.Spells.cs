using League.Models;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    internal static (JObject? Popular, JObject? Winning) SelectSpellRecommendations(JArray options)
    {
        var valid = options
            .OfType<JObject>()
            .Where(o => o["ids"] is JArray ids && ids.Count == 2 && ids.Values<int>().All(id => id > 0))
            .ToArray();
        double? Win(JObject row) => AramMetaIndex.Rate(row["win_rate"]) ?? OpggBuild.Ratio(row["win"], row["play"]);
        var popular = valid
            .Where(o => AramMetaIndex.Rate(o["pick_rate"]) != null)
            .OrderByDescending(o => AramMetaIndex.Rate(o["pick_rate"]))
            .ThenByDescending(o => (long?)o["play"] ?? 0)
            .FirstOrDefault();
        var winning = valid
            .Where(o => Win(o) is >= 0 and <= 1)
            .OrderByDescending(Win)
            .ThenByDescending(o => (long?)o["play"] ?? 0)
            .FirstOrDefault();
        return (popular, winning);
    }

    private async Task AddSpellRecommendations(JArray options, CancellationToken token)
    {
        var selected = SelectSpellRecommendations(options);
        var columns = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(282, 0),
            Margin = new Padding(0, 0, 0, 12),
        };
        _body.Controls.Add(columns);
        foreach (
            var (title, option, pick) in new[]
            {
                ("使用率最高", selected.Popular, true),
                ("胜率最高", selected.Winning, false),
            }
        )
        {
            token.ThrowIfCancellationRequested();
            var card = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(134, 0),
                MaximumSize = new Size(134, 0),
                Padding = new Padding(5, 5, 5, 8),
                Margin = new Padding(0, 0, 5, 0),
                BackColor = Color.FromArgb(13, 38, 50),
            };
            columns.Controls.Add(card);
            card.Controls.Add(
                new Label
                {
                    Text = title,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(208, 185, 130),
                    Margin = new Padding(0, 0, 0, 4),
                }
            );
            if (option == null)
            {
                card.Controls.Add(new Label { Text = "暂无数据", AutoSize = true });
                continue;
            }
            await IconRow(((JArray)option["ids"]!).Values<int>(), "spell", token, card);
            double? rate = pick
                ? AramMetaIndex.Rate(option["pick_rate"])
                : AramMetaIndex.Rate(option["win_rate"]) ?? OpggBuild.Ratio(option["win"], option["play"]);
            var label = new Label
            {
                Text = (pick ? "使用率 " : "胜率 ") + $"{rate:P2}",
                AutoSize = true,
                ForeColor = Color.FromArgb(195, 213, 219),
                Margin = new Padding(0, 3, 0, 0),
            };
            card.Controls.Add(label);
            _tips.SetToolTip(
                label,
                $"点击应用这组召唤师技能，保留已有技能的按键位置。\n样本：{((long?)option["play"])?.ToString("N0") ?? "未知"} 场。\n小样本胜率可能波动较大。"
            );
            int[] spells = ((JArray)option["ids"]!).Values<int>().ToArray();
            int hero = _hero;
            void MakeClickable(Control control)
            {
                control.Cursor = Cursors.Hand;
                control.Click += async (_, _) => await ApplySpellsAsync(spells, hero, token);
                foreach (Control child in control.Controls)
                {
                    MakeClickable(child);
                }
            }
            MakeClickable(card);
            _tips.SetToolTip(card, "点击应用这组召唤师技能");
        }
        // FlowLayoutPanel can exclude trailing padding/margins from its scroll extent.
        // A real final control keeps the last rate clear of the fixed action bar.
        _body.Controls.Add(new Panel { Size = new Size(1, 8), Margin = Padding.Empty });
    }

    private bool _applyingSpells;

    private async Task ApplySpellsAsync(int[] spells, int hero, CancellationToken token)
    {
        if (_applyingSpells || token.IsCancellationRequested || hero <= 0)
        {
            return;
        }

        _applyingSpells = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            _status.Text = "正在配置召唤师技能…";
            var client = FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端已断开。");
            await Services.ChampionSelectSpellService.ApplyAsync(client, spells, hero, timeout.Token);
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _status.Text = "召唤师技能已配置并校验成功。";
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _status.Text = ex is OperationCanceledException ? "召唤师技能配置超时，请检查客户端。" : ex.Message;
                _tips.SetToolTip(_status, _status.Text);
            }
        }
        finally
        {
            _applyingSpells = false;
        }
    }
}
