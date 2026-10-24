using League.Controls;
using Newtonsoft.Json.Linq;

namespace League.UIHelpers
{
    internal static class WeGameHonorIcons
    {
        private static readonly Lazy<Bitmap> Sprite = new(() =>
        {
            using var stream = typeof(WeGameHonorIcons).Assembly.GetManifestResourceStream(
                "League.WeGame.honor16.png"
            )!;
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        });

        public static Bitmap Get(string key)
        {
            var icon = new Bitmap(SlotSize, SlotSize);
            using var graphics = Graphics.FromImage(icon);
            if (key == "firstBlood")
            {
                // A local first-blood symbol; not a claimed WeGame sprite mapping. Same 10 px box as the sprite icons.
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var drop = new System.Drawing.Drawing2D.GraphicsPath();
                drop.AddBezier(8, 3, 7.4f, 5, 4, 7.5f, 4, 9.5f);
                drop.AddBezier(4, 9.5f, 4, 14, 12, 14, 12, 9.5f);
                drop.AddBezier(12, 9.5f, 12, 7.5f, 8.6f, 5, 8, 3);
                using var brush = new SolidBrush(Theme.Loss);
                graphics.FillPath(brush, drop);
                return icon;
            }

            // Cells of WeGame's current honor16 sprite, 26 px apart; each icon is 10 x 10.
            int cell = key switch
            {
                "assists" => 0,
                "taken" => 2,
                "damage" => 3,
                "triple" => 5,
                "quadra" => 6,
                "penta" => 7,
                "gold" => 9,
                "towers" => 10,
                "cs" => 11,
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };
            // Drawn 1:1 in the middle of the badge slot: scaling a 10 px icon only blurs it, and the margin keeps
            // the compact layout's 14 px step from covering the next icon.
            int offset = (SlotSize - IconSize) / 2;
            lock (Sprite)
            {
                graphics.DrawImage(
                    Sprite.Value,
                    new Rectangle(offset, offset, IconSize, IconSize),
                    new Rectangle(cell * CellStep, 0, IconSize, IconSize),
                    GraphicsUnit.Pixel
                );
            }
            return icon;
        }

        private const int SlotSize = 16;
        private const int IconSize = 10;
        private const int CellStep = 26;

        public static List<(string Key, string Hint)> Select(JObject player, List<JObject> all)
        {
            var result = new List<(string Key, string Hint)>();
            // WeGame's public script displays only the highest multi-kill badge.
            foreach (
                var (field, key, name) in new[]
                {
                    ("pentaKills", "penta", "五杀"),
                    ("quadraKills", "quadra", "四杀"),
                    ("tripleKills", "triple", "三杀"),
                }
            )
            {
                int count = player[field]?.Value<int>() ?? 0;
                if (count > 0)
                {
                    result.Add((key, $"{name} · {count} 次"));
                    break;
                }
            }
            foreach (
                var (field, key, name) in new[]
                {
                    ("assists", "assists", "助攻"),
                    ("goldEarned", "gold", "经济"),
                    ("totalDamageDealtToChampions", "damage", "对英雄伤害"),
                    ("totalDamageTaken", "taken", "承受伤害"),
                    ("totalMinionsKilled", "cs", "补兵"),
                    ("turretKills", "towers", "摧毁防御塔"),
                }
            )
            {
                // Missing data must not produce an award. These are explicitly match-wide comparisons,
                // not an attempt to reproduce WeGame's unpublished server-side battle_tag rules.
                if (all.Count == 0 || all.Any(p => p[field]?.Type != JTokenType.Integer))
                {
                    continue;
                }

                int value = player[field]?.Value<int>() ?? 0;
                if (value > 0 && value == all.Max(p => p[field]!.Value<int>()))
                {
                    result.Add((key, $"全场{name}最多：{value:N0}（并列均显示；按本场数据统计）"));
                }
            }
            return result;
        }
    }
}
