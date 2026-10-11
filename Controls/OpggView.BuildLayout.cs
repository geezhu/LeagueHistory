using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class OpggView
{
    private FlowLayoutPanel Inline(Control parent)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 2, 0, 2),
            MinimumSize = new Size(200, 0),
            MaximumSize = new Size(Math.Max(280, parent.Width - 24), 0),
        };
        parent.Controls.Add(row);
        return row;
    }

    private Label TextLabel(string text, Color? color = null) =>
        new()
        {
            AutoSize = true,
            Text = text,
            ForeColor = color ?? Color.FromArgb(56, 74, 94),
            Margin = new Padding(8, 8, 8, 4),
        };

    private async Task RenderAsync(OpggBuild build, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_closing)
        {
            return;
        }
        _tiers.Visible = false;
        _content.Visible = true;
        await RenderChampionSummaryAsync(build, token);
        if (build.Query.Mode != "arena")
        {
            await RenderOptionsAsync(Section("召唤师技能"), build.Data["summoner_spells"], "spell", 2, token);
        }
        await RenderRunesAsync(build, token);
        token.ThrowIfCancellationRequested();
        RenderSkills(build);
        await RenderStartingEquipmentAsync(build, token);
        await RenderLaterEquipmentAsync(build, token);
        await RenderOptionsAsync(Section("核心装备组合"), build.Data["core_items"], "item", 5, token);
        await RenderMatchupsAsync(build, token);
        if (build.Query.Mode == "arena")
        {
            await RenderArenaPartnersAsync(build, token);
            await RenderArenaAugmentsAsync(build, token);
        }
    }

    private async Task RenderChampionSummaryAsync(OpggBuild build, CancellationToken token)
    {
        var summary = Section(ChampionName(build.Query.ChampionId));
        summary.BackColor = Color.FromArgb(226, 241, 252);
        summary.Controls.OfType<Label>().First().Font = _championHeadingFont ??= new Font(
            Font.FontFamily,
            17,
            FontStyle.Bold
        );
        var top = Inline(summary);
        await AddIconAsync(top, build.Query.ChampionId, "champion", token);
        token.ThrowIfCancellationRequested();
        if (top.Controls.OfType<PictureBox>().FirstOrDefault() is { } hero)
        {
            hero.Size = new Size(52, 52);
        }
        var stats = build.Stats;
        double? win = (double?)stats["win_rate"] ?? OpggBuild.Ratio(stats["win"], stats["play"]);
        top.Controls.Add(TextLabel($"胜率\n{Percent(win)}", Color.FromArgb(0, 148, 165)));
        top.Controls.Add(TextLabel($"登场率\n{Percent((double?)stats["pick_rate"])}"));
        top.Controls.Add(TextLabel($"禁用率\n{Percent((double?)stats["ban_rate"])}"));
        top.Controls.Add(TextLabel($"梯队\nT{stats["tier_data"]?["tier"] ?? stats["tier"]}"));
        top.Controls.Add(TextLabel($"统计场次\n{stats["play"]?.ToString() ?? "—"}"));
        if (build.Query.Mode == "arena")
        {
            top.Controls.Add(
                TextLabel(
                    $"第一名 {Percent(OpggBuild.Ratio(stats["first_place"], stats["play"]))}\n平均名次 {OpggBuild.Ratio(stats["total_place"], stats["play"]):0.00}"
                )
            );
        }
    }

    private async Task RenderLaterEquipmentAsync(OpggBuild build, CancellationToken token)
    {
        var strip = Inline(Section("后续装备"));
        foreach (
            var id in (build.Data["last_items"] as JArray ?? new())
                .OfType<JObject>()
                .Take(16)
                .Select(o => (int?)o["ids"]?.First ?? 0)
                .Where(id => id > 0)
        )
        {
            await AddIconAsync(strip, id, "item", token);
        }
    }

    private async Task RenderArenaPartnersAsync(OpggBuild build, CancellationToken token)
    {
        var synergies = Section("竞技场搭档");
        foreach (var synergy in (build.Data["synergies"] as JArray ?? new()).OfType<JObject>().Take(6))
        {
            var row = Inline(synergies);
            await AddIconAsync(row, (int?)synergy["champion_id"] ?? 0, "champion", token);
            token.ThrowIfCancellationRequested();
            row.Controls.Add(
                TextLabel(
                    $"胜率 {Percent(OpggBuild.Ratio(synergy["win"], synergy["play"]))} · 第一名 {Percent(OpggBuild.Ratio(synergy["first_place"], synergy["play"]))}"
                )
            );
        }
    }

    private async Task RenderArenaAugmentsAsync(OpggBuild build, CancellationToken token)
    {
        var augments = Section("竞技场强化符文");
        foreach (var group in (build.Data["augment_group"] as JArray ?? new()).OfType<JObject>())
        {
            foreach (var augment in (group["augments"] as JArray ?? new()).OfType<JObject>().Take(5))
            {
                var row = Inline(augments);
                await AddIconAsync(row, (int?)augment["id"] ?? 0, "augment", token);
                token.ThrowIfCancellationRequested();
                row.Controls.Add(
                    TextLabel(
                        $"胜率 {Percent(OpggBuild.Ratio(augment["win"], augment["play"]))} · 第一名 {Percent(OpggBuild.Ratio(augment["first_place"], augment["play"]))}"
                    )
                );
            }
        }
    }

    private async Task RenderOptionsAsync(
        Control section,
        JToken? data,
        string kind,
        int limit,
        CancellationToken token
    )
    {
        var options = (data as JArray ?? new()).OfType<JObject>().Take(limit).ToArray();
        if (options.Length == 0)
        {
            section.Controls.Add(TextLabel("暂无数据"));
        }

        foreach (var option in options)
        {
            token.ThrowIfCancellationRequested();
            var row = Inline(section);
            foreach (var id in (option["ids"] as JArray ?? new()).Values<int>())
            {
                await AddIconAsync(row, id, kind, token);
            }

            token.ThrowIfCancellationRequested();
            row.Controls.Add(
                TextLabel(
                    $"胜率 {Percent(OpggBuild.Ratio(option["win"], option["play"]))}\n{option["play"]?.ToString() ?? "—"} 场"
                )
            );
            row.Controls.Add(
                TextLabel($"选用率\n{Percent((double?)option["pick_rate"])}", Color.FromArgb(0, 153, 166))
            );
        }
    }

    private async Task RenderRunesAsync(OpggBuild build, CancellationToken token)
    {
        var section = Section("推荐符文");
        if (build.Runes.Length == 0)
        {
            section.Controls.Add(TextLabel("此模式暂无符文方案"));
            return;
        }
        var recommendations = new OpggRuneRecommendations(
            build.Runes.Take(3).ToArray(),
            async (plan, cancellation) => (await ReadResourceAsync(plan.Perks[0], "rune", cancellation)).image,
            () => FormMain.Globals.resLoading?.Runes,
            () => !_closing && _displayed == build,
            (plan, cancellation) => ApplyRunePlanAsync(build, plan, cancellation),
            message =>
            {
                if (!_closing && !token.IsCancellationRequested)
                {
                    _status.Text = message;
                }
            },
            token
        );
        section.Controls.Add(recommendations);
        await recommendations.InitializeAsync();
    }

    private static Task ApplyRunePlanAsync(OpggBuild build, OpggRunes runes, CancellationToken token)
    {
        var client =
            FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("请先连接英雄联盟客户端。");
        return OpggRuneService.ApplyAsync(client, runes, ChampionName(build.Query.ChampionId), token);
    }
}
