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
        _tiers.Visible = false;
        _content.Visible = true;
        var summary = Section(ChampionName(build.Query.ChampionId));
        summary.BackColor = Color.FromArgb(226, 241, 252);
        var heading = summary.Controls.OfType<Label>().First();
        heading.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
        var top = Inline(summary);
        await AddIconAsync(top, build.Query.ChampionId, "champion", token);
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

        if (build.Query.Mode != "arena")
        {
            await RenderOptionsAsync(Section("召唤师技能"), build.Data["summoner_spells"], "spell", 2, token);
        }

        await RenderRunesAsync(build, token);

        RenderSkills(build);
        await RenderStartingEquipmentAsync(build, token);
        var later = Section("后续装备");
        var strip = Inline(later);
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

        await RenderOptionsAsync(Section("核心装备组合"), build.Data["core_items"], "item", 5, token);
        await RenderMatchupsAsync(build, token);
        if (build.Query.Mode == "arena")
        {
            var synergies = Section("竞技场搭档");
            foreach (var syn in (build.Data["synergies"] as JArray ?? new()).OfType<JObject>().Take(6))
            {
                var row = Inline(synergies);
                await AddIconAsync(row, (int?)syn["champion_id"] ?? 0, "champion", token);
                row.Controls.Add(
                    TextLabel(
                        $"胜率 {Percent(OpggBuild.Ratio(syn["win"], syn["play"]))} · 第一名 {Percent(OpggBuild.Ratio(syn["first_place"], syn["play"]))}"
                    )
                );
            }
            var augments = Section("竞技场强化符文");
            foreach (var group in (build.Data["augment_group"] as JArray ?? new()).OfType<JObject>())
            {
                foreach (var aug in (group["augments"] as JArray ?? new()).OfType<JObject>().Take(5))
                {
                    var row = Inline(augments);
                    await AddIconAsync(row, (int?)aug["id"] ?? 0, "augment", token);
                    row.Controls.Add(
                        TextLabel(
                            $"胜率 {Percent(OpggBuild.Ratio(aug["win"], aug["play"]))} · 第一名 {Percent(OpggBuild.Ratio(aug["first_place"], aug["play"]))}"
                        )
                    );
                }
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
        var plans = build.Runes.Take(3).ToArray();
        var layout = Inline(section);
        var preview = new Panel { Size = new Size(420, 222), Margin = Padding.Empty };
        var choices = new FlowLayoutPanel
        {
            Width = 254,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(12, 0, 0, 0),
        };
        layout.Controls.Add(preview);
        layout.Controls.Add(choices);
        int selected = 0,
            revision = 0;
        bool applying = false;
        var buttons = new List<Button>();
        var apply = new Button
        {
            Text = "应用到工具专用符文页",
            Size = new Size(250, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 158, 171),
            ForeColor = Color.White,
            Margin = new Padding(0, 6, 0, 0),
        };
        apply.FlatAppearance.BorderSize = 0;
        async Task SelectPlan(int index)
        {
            selected = index;
            int current = ++revision;
            apply.Enabled = false;
            foreach (var button in buttons)
            {
                bool active = (int)button.Tag! == index;
                button.BackColor = active ? Color.FromArgb(225, 244, 247) : Color.FromArgb(245, 247, 249);
                button.FlatAppearance.BorderColor = active
                    ? Color.FromArgb(0, 172, 188)
                    : Color.FromArgb(235, 239, 242);
            }
            var tree = new OpggRuneTree();
            try
            {
                await tree.SetPlanAsync(plans[index], FormMain.Globals.resLoading?.Runes, token);
                if (current != revision || preview.IsDisposed || token.IsCancellationRequested)
                {
                    tree.Dispose();
                    return;
                }
                foreach (Control old in preview.Controls.Cast<Control>().ToArray())
                {
                    old.Dispose();
                }

                preview.Controls.Add(tree);
                apply.Enabled = !applying;
            }
            catch
            {
                tree.Dispose();
                if (!token.IsCancellationRequested)
                {
                    throw;
                }
            }
        }
        for (int i = 0; i < plans.Length; i++)
        {
            int index = i;
            var plan = plans[i];
            var button = new Button
            {
                Text = "",
                Size = new Size(250, 54),
                Tag = i,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 0, 4),
                AccessibleName = $"方案 {i + 1}，胜率 {Percent(plan.WinRate)}，选用率 {Percent(plan.PickRate)}",
            };
            Image? icon = null;
            var resources = FormMain.Globals.resLoading;
            if (resources != null)
            {
                var task = resources.GetRuneInfoAsync(plan.Perks[0]);
                try
                {
                    icon = (await task.WaitAsync(token)).image;
                }
                catch (OperationCanceledException)
                {
                    _ = task.ContinueWith(
                        t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                            {
                                t.Result.image?.Dispose();
                            }
                        },
                        TaskScheduler.Default
                    );
                    throw;
                }
            }
            button.Disposed += (_, _) => icon?.Dispose();
            button.Paint += (_, e) =>
            {
                if (icon != null)
                {
                    e.Graphics.DrawImage(icon, new Rectangle(8, 11, 32, 32));
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    Percent(plan.WinRate),
                    Font,
                    new Rectangle(46, 6, 110, 21),
                    Color.FromArgb(45, 61, 75),
                    TextFormatFlags.HorizontalCenter
                );
                TextRenderer.DrawText(
                    e.Graphics,
                    $"{plan.Plays:N0} 场",
                    Font,
                    new Rectangle(46, 28, 110, 20),
                    Color.Gray,
                    TextFormatFlags.HorizontalCenter
                );
                TextRenderer.DrawText(
                    e.Graphics,
                    Percent(plan.PickRate),
                    Font,
                    new Rectangle(158, 16, 85, 22),
                    Color.FromArgb(0, 158, 171),
                    TextFormatFlags.HorizontalCenter
                );
            };
            _tips.SetToolTip(button, "中间为胜率和统计场次，右侧为选用率。点击切换符文方案。");
            button.Click += async (_, _) =>
            {
                try
                {
                    await SelectPlan(index);
                }
                catch (Exception ex)
                {
                    if (!IsDisposed)
                    {
                        _status.Text = "符文预览加载失败：" + ex.Message;
                    }
                }
            };
            buttons.Add(button);
            choices.Controls.Add(button);
        }
        choices.Controls.Add(apply);
        apply.Click += async (_, _) =>
        {
            if (_displayed != build || applying)
            {
                return;
            }

            var runes = plans[selected];
            applying = true;
            apply.Enabled = false;
            foreach (var button in buttons)
            {
                button.Enabled = false;
            }

            try
            {
                var client =
                    FormMain.Globals.lcuClient?.Client
                    ?? throw new InvalidOperationException("请先连接英雄联盟客户端。");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await OpggRuneService.ApplyAsync(client, runes, ChampionName(build.Query.ChampionId), timeout.Token);
                if (!apply.IsDisposed)
                {
                    apply.Text = "已应用到工具专用符文页";
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !token.IsCancellationRequested)
                {
                    MessageBox.Show(
                        FindForm(),
                        ex is OperationCanceledException ? "请求超时，请在客户端确认是否生效。" : ex.Message,
                        "应用符文"
                    );
                }
            }
            finally
            {
                applying = false;
                if (!apply.IsDisposed)
                {
                    apply.Enabled = true;
                }

                foreach (var button in buttons)
                {
                    if (!button.IsDisposed)
                    {
                        button.Enabled = true;
                    }
                }
            }
        };
        await SelectPlan(0);
    }
}
