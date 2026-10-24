using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.UIState
{
    public partial class DetailedViewLayoutManager
    {
        private sealed class HighlightKdaLabel : Label
        {
            public Color HighlightColor { get; init; }
            public int KillsLength { get; init; }
            public int AssistsStart { get; init; } = -1;

            protected override void OnPaint(PaintEventArgs e)
            {
                const TextFormatFlags flags =
                    TextFormatFlags.SingleLine
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.EndEllipsis;
                // Draw one continuous string. Repaint only selected glyphs in another color;
                // separators, font weight, alignment and spacing remain identical.
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
                int Measure(string text) =>
                    text.Length == 0
                        ? 0
                        : TextRenderer
                            .MeasureText(
                                e.Graphics,
                                text,
                                Font,
                                new Size(int.MaxValue, int.MaxValue),
                                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                            )
                            .Width;
                int padding =
                    (
                        TextRenderer
                            .MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), flags)
                            .Width - Measure(Text)
                    ) / 2;
                void Highlight(int start, int length)
                {
                    if (length <= 0)
                    {
                        return;
                    }

                    var state = e.Graphics.Save();
                    try
                    {
                        e.Graphics.SetClip(
                            new Rectangle(
                                padding + Measure(Text[..start]),
                                0,
                                Measure(Text.Substring(start, length)),
                                Height
                            )
                        );
                        TextRenderer.DrawText(
                            e.Graphics,
                            Text,
                            Font,
                            ClientRectangle,
                            HighlightColor,
                            flags | TextFormatFlags.PreserveGraphicsClipping
                        );
                    }
                    finally
                    {
                        e.Graphics.Restore(state);
                    }
                }
                Highlight(0, KillsLength);
                if (AssistsStart >= 0)
                {
                    Highlight(AssistsStart, Text.Length - AssistsStart);
                }
            }
        }

        private void CreateScoreboardRow(
            Panel row,
            JObject player,
            string name,
            bool isSelf,
            (
                int damage,
                int damageRank,
                int gold,
                int goldRank,
                double kp,
                int kpRank,
                int damageTaken,
                int damageTakenRank
            ) stats,
            MatchInfo match
        )
        {
            var highlight = ScoreboardHighlight(player);
            AddScoreboardProfile(row, player, name, isSelf, match);
            AddScoreboardLoadout(row, player, match);
            AddScoreboardKda(row, player, match, highlight);
            AddScoreboardMetric(
                row,
                player,
                match,
                highlight,
                "goldEarned",
                stats.gold,
                stats.goldRank,
                686,
                Color.FromArgb(190, 166, 113)
            );
            AddScoreboardMetric(
                row,
                player,
                match,
                highlight,
                "totalDamageDealtToChampions",
                stats.damage,
                stats.damageRank,
                780,
                Theme.Accent
            );

            AddScoreboardSecondaryMetrics(
                row,
                player,
                match,
                stats.kp,
                stats.kpRank,
                stats.damageTaken,
                stats.damageTakenRank,
                highlight
            );
            AddScoreboardEstimate(row, player, match, highlight);
        }

        /// <summary>
        /// The estimated score column, right of the six metrics as WeGame places its own. It is not paged: the compact
        /// board keeps it at the right edge while the metrics switch.
        /// </summary>
        public const int ScoreColumnLeft = 1124;

        public const int ScoreColumnWidth = 56;

        /// <summary>The width the column takes from the board, gap included.</summary>
        public const int ScoreColumnReserve = 60;

        /// <summary>The narrower column of a compact board, which leaves more room for the paged metrics.</summary>
        public const int CompactScoreColumnReserve = 48;

        private void AddScoreboardEstimate(Panel row, JObject player, MatchInfo match, Color highlight)
        {
            int index = match.AllParticipants.IndexOf(player);
            if (index < 0 || Services.AramMayhemScoreEstimator.Shared.Estimate(match) is not double[] estimates)
            {
                return;
            }

            double estimate = estimates[index];
            int rank = estimates.Count(other => other > estimate) + 1;
            var label = ScoreboardText(
                row,
                $"{estimate:0.0}",
                ScoreColumnLeft,
                ScoreColumnWidth,
                rank == 1 ? highlight : ScoreboardInk,
                rank == 1
            );
            label.Name = "ScoreEstimate";
            label.Font = Theme.NumberFont(12f, strong: rank == 1);
            label.TextAlign = ContentAlignment.MiddleCenter;
            _toolTip.SetToolTip(
                label,
                $"估算评分 {estimate:0.0} · 全场第 {rank} 名\n"
                    + "按 WeGame 海克斯大乱斗评分拟合的估算，不是 WeGame 的数据；平均相差约 0.7 分。"
            );
            AddScoreboardHonour(row, Services.AramMayhemScoreEstimator.Honour(match.AllParticipants, estimates, index));
        }

        // WeGame's MVP or SVP mark in the bottom right corner of the 34 px champion icon at (12, 5), where WeGame puts it.
        private void AddScoreboardHonour(Panel row, MatchHonour honour)
        {
            if (League.UIHelpers.WeGameHonorIcons.Badge(honour) is not Bitmap badge)
            {
                return;
            }

            var picture = new PictureBox
            {
                Name = "MatchHonour",
                Location = new Point(12 + 34 - badge.Width, 5 + 34 - badge.Height),
                Size = badge.Size,
                // A copy: the scoreboard disposes the images of its picture boxes, and the badge is shared.
                Image = new Bitmap(badge),
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = Color.Transparent,
            };
            row.Controls.Add(picture);
            // Added after the icon, so it is raised to show over it.
            picture.BringToFront();
            _toolTip.SetToolTip(picture, League.UIHelpers.WeGameHonorIcons.BadgeHint(honour));
        }

        private static readonly Color ScoreboardInk = Theme.Ink;
        private static readonly Color ScoreboardMuted = Theme.Muted;

        private Label ScoreboardText(Panel row, string text, int x, int width, Color color, bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, 4),
                Size = new Size(width, 30),
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.NumberFont(10f, bold),
            };
            row.Controls.Add(label);
            _toolTip.SetToolTip(label, text);
            return label;
        }

        // Augment art is light on transparency, so like the match list it sits on a dark plate to stay legible.
        private void ScoreboardIcon(Panel row, Image? image, string? hint, int x, int y, int size, bool dark = false)
        {
            var picture = new PictureBox
            {
                Location = new Point(x, y),
                Size = new Size(size, size),
                BackColor = dark ? Color.FromArgb(40, 49, 63) : Color.FromArgb(239, 243, 248),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = image == null ? null : new Bitmap(image),
            };
            row.Controls.Add(picture);
            _toolTip.SetToolTip(picture, StripHtmlTags(hint ?? ""));
        }

        private void CachedScoreboardIcon(
            Panel row,
            Dictionary<int, (Image, string, string)> cache,
            int id,
            int x,
            int y,
            int size
        )
        {
            if (cache.TryGetValue(id, out var data))
            {
                ScoreboardIcon(row, data.Item1, data.Item2 + "\n" + data.Item3, x, y, size);
            }
            else
            {
                ScoreboardIcon(row, null, id > 0 ? "资源暂未加载" : "空槽位", x, y, size);
            }
        }

        private void AddScoreboardProfile(Panel row, JObject player, string name, bool isSelf, MatchInfo match)
        {
            if (isSelf)
            {
                row.Controls.Add(new Panel { BackColor = Theme.Accent, Size = new Size(3, 44) });
            }

            CachedScoreboardIcon(row, _championCache, player["championId"]?.Value<int>() ?? 0, 12, 5, 34);
            var nameLabel = new League.Controls.PlayerNameLink
            {
                Name = "PlayerName",
                PlayerName = name,
                Text = name,
                SummonerId = player["summonerId"]?.ToString() ?? "",
                ResolveProfile = ResolvePlayerProfile,
                Location = new Point(58, 4),
                Size = new Size(180, 30),
                Font = new Font("Microsoft YaHei UI", 9f, isSelf ? FontStyle.Bold : FontStyle.Regular),
                LinkColor = isSelf ? Theme.Accent : ScoreboardInk,
                ActiveLinkColor = Theme.Accent,
                ForeColor = ScoreboardInk,
            };
            nameLabel.ProfileRequested += id => PlayerProfileRequested?.Invoke(id);
            row.Controls.Add(nameLabel);
            _toolTip.SetToolTip(nameLabel, name + "\n点击查看玩家主页；🔒 表示生涯不公开");
            var badges = League.UIHelpers.WeGameHonorIcons.Select(player, match.AllParticipants);
            bool firstBlood = player["firstBloodKill"]?.Value<bool>() == true;
            if (firstBlood)
            {
                badges.Insert(0, ("firstBlood", "获得本场首杀"));
            }

            if (firstBlood || badges.Count > 0)
            {
                nameLabel.SetBounds(58, 1, 180, 22);
                int badgeX = 58;
                foreach (var badge in badges)
                {
                    var picture = new PictureBox
                    {
                        Name = "Honor_" + badge.Key,
                        Location = new Point(badgeX, 24),
                        Size = new Size(16, 16),
                        Image = League.UIHelpers.WeGameHonorIcons.Get(badge.Key),
                        SizeMode = PictureBoxSizeMode.Normal,
                        BackColor = Color.Transparent,
                    };
                    row.Controls.Add(picture);
                    _toolTip.SetToolTip(picture, badge.Hint);
                    badgeX += 16;
                }
            }
        }

        private void AddScoreboardLoadout(Panel row, JObject player, MatchInfo match)
        {
            for (int i = 1; i <= 2; i++)
            {
                CachedScoreboardIcon(
                    row,
                    _spellCache,
                    player[$"spell{i}Id"]?.Value<int>() ?? 0,
                    242,
                    11 + (i - 1) * 12,
                    10
                );
            }

            for (int i = 0; i < 7; i++)
            {
                CachedScoreboardIcon(row, _itemCache, player[$"item{i}"]?.Value<int>() ?? 0, 258 + i * 24, 11, 22);
            }

            if (IsHextechArenaMode(player))
            {
                var augments = GetSelfAugments(player, match);
                for (int i = 0; i < Math.Min(6, augments.Length); i++)
                {
                    ScoreboardIcon(
                        row,
                        augments[i]?.Icon,
                        augments[i]?.name + "\n" + augments[i]?.longDesc,
                        436 + i * 24,
                        11,
                        22,
                        dark: true
                    );
                }
            }
            else
            {
                var runes = LoadRunesForParticipant(player);
                var all = (runes.Primary ?? Array.Empty<RuneInfo>())
                    .Concat(runes.Secondary ?? Array.Empty<RuneInfo>())
                    .ToArray();
                for (int i = 0; i < Math.Min(6, all.Length); i++)
                {
                    CachedScoreboardIcon(row, _runeCache, all[i].RuneId, 436 + i * 24, 11, 22);
                }
                // Retain access to the stat shards without expanding every row.
                string shards = string.Join(
                    "\n",
                    (runes.Shards ?? Array.Empty<RuneInfo>()).Select(r =>
                        _runeCache.TryGetValue(r.RuneId, out var data)
                            ? data.Item2 + ": " + StripHtmlTags(data.Item3)
                            : "属性碎片 " + r.RuneId
                    )
                );
                if (shards.Length > 0)
                {
                    _toolTip.SetToolTip(row, shards);
                }
            }
        }

        private static bool IsScoreboardMaximum(JObject player, MatchInfo match, string key)
        {
            if (match.AllParticipants.Count == 0 || match.AllParticipants.Any(p => p[key]?.Type != JTokenType.Integer))
            {
                return false;
            }

            int value = player[key]?.Value<int>() ?? 0;
            return value > 0 && value == match.AllParticipants.Max(p => p[key]!.Value<int>());
        }

        private static Color ScoreboardHighlight(JObject player) =>
            player["win"]?.Value<bool>() == true ? Theme.Win
            : player["win"]?.Value<bool>() == false ? Theme.Loss
            : Theme.Accent;

        private void AddScoreboardKda(Panel row, JObject player, MatchInfo match, Color highlight)
        {
            string kills = player["kills"]?.ToString() ?? "—",
                deaths = player["deaths"]?.ToString() ?? "—",
                assists = player["assists"]?.ToString() ?? "—";
            var kda = new HighlightKdaLabel
            {
                Name = "KdaValues",
                Text = $"{kills} / {deaths} / {assists}",
                Location = new Point(592, 4),
                Size = new Size(90, 30),
                ForeColor = ScoreboardInk,
                BackColor = Color.Transparent,
                Font = Theme.NumberFont(10f, strong: true),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                HighlightColor = highlight,
                KillsLength = IsScoreboardMaximum(player, match, "kills") ? kills.Length : 0,
                AssistsStart = IsScoreboardMaximum(player, match, "assists") ? kills.Length + deaths.Length + 6 : -1,
            };
            row.Controls.Add(kda);
            _toolTip.SetToolTip(kda, "击杀 / 死亡 / 助攻；彩色数字表示全场最高（并列均高亮）");
        }

        private void AddScoreboardMetric(
            Panel row,
            JObject player,
            MatchInfo match,
            Color highlight,
            string key,
            int value,
            int rank,
            int x,
            Color color
        )
        {
            int maximum = match.AllParticipants.Max(p => p[key]?.Value<int>() ?? 0);
            bool leading = IsScoreboardMaximum(player, match, key);
            var label = ScoreboardText(
                row,
                $"{value / 1000d:0.0}k",
                x,
                86,
                leading ? highlight : ScoreboardInk,
                leading
            );
            label.Name = key;
            _toolTip.SetToolTip(label, $"{value:N0} · 全场第 {rank} 名" + (leading ? " · 全场最高（并列均高亮）" : ""));
            var bar = new Panel
            {
                Location = new Point(x, 36),
                Size = new Size(76, 3),
                BackColor = Color.FromArgb(231, 237, 245),
            };
            int filled = maximum > 0 && value > 0 ? Math.Max(1, (int)(76d * value / maximum)) : 0;
            bar.Paint += (_, e) =>
            {
                using var brush = new SolidBrush(color);
                e.Graphics.FillRectangle(brush, 0, 0, filled, 3);
            };
            row.Controls.Add(bar);
        }

        private static bool IsScoreboardDerivedMaximum(JObject player, MatchInfo match, Func<JObject, double?> selector)
        {
            var values = match.AllParticipants.Select(selector).ToArray();
            var value = selector(player);
            return value > 0 && values.Length > 0 && values.All(v => v.HasValue) && value == values.Max();
        }

        private void AddScoreboardSecondaryMetrics(
            Panel row,
            JObject player,
            MatchInfo match,
            double participation,
            int kpRank,
            int damageTaken,
            int damageTakenRank,
            Color highlight
        )
        {
            double? Participation(JObject p) =>
                p["challenges"]?["killParticipation"] is JToken token
                && token.Type is JTokenType.Float or JTokenType.Integer
                    ? token.Value<double>()
                    : null;
            double? Minions(JObject p) =>
                p["totalMinionsKilled"]?.Type == JTokenType.Integer
                && p["neutralMinionsKilled"]?.Type == JTokenType.Integer
                    ? p["totalMinionsKilled"]!.Value<double>() + p["neutralMinionsKilled"]!.Value<double>()
                    : null;
            bool topKp = IsScoreboardDerivedMaximum(player, match, Participation),
                topCs = IsScoreboardDerivedMaximum(player, match, Minions),
                topTaken = IsScoreboardMaximum(player, match, "totalDamageTaken");
            string Highest(bool leading) => leading ? " · 全场最高（并列均高亮）" : "";
            var kp = ScoreboardText(row, $"{participation:P1}", 880, 70, topKp ? highlight : ScoreboardMuted, topKp);
            kp.Name = "killParticipation";
            _toolTip.SetToolTip(kp, $"参团率 {participation:P1} · 全场第 {kpRank} 名" + Highest(topKp));
            int cs =
                (player["totalMinionsKilled"]?.Value<int>() ?? 0) + (player["neutralMinionsKilled"]?.Value<int>() ?? 0);
            var minions = ScoreboardText(row, $"{cs}", 958, 60, topCs ? highlight : ScoreboardMuted, topCs);
            minions.Name = "totalCs";
            _toolTip.SetToolTip(minions, $"补刀 {cs:N0}（小兵 + 野怪）" + Highest(topCs));
            var taken = ScoreboardText(
                row,
                $"{damageTaken / 1000d:0.0}k",
                1026,
                90,
                topTaken ? highlight : ScoreboardMuted,
                topTaken
            );
            taken.Name = "totalDamageTaken";
            _toolTip.SetToolTip(taken, $"承受伤害 {damageTaken:N0} · 全场第 {damageTakenRank} 名" + Highest(topTaken));
        }
    }
}
