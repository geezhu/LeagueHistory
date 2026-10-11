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
            if (key == "firstBlood")
            {
                // A local first-blood symbol; not a claimed WeGame sprite mapping.
                var icon = new Bitmap(16, 16);
                using var graphics = Graphics.FromImage(icon);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var drop = new System.Drawing.Drawing2D.GraphicsPath();
                drop.AddBezier(8, 2, 7, 5, 3, 8, 3, 10);
                drop.AddBezier(3, 10, 3, 16, 13, 16, 13, 10);
                drop.AddBezier(13, 10, 13, 8, 9, 5, 8, 2);
                using var brush = new SolidBrush(Color.FromArgb(191, 81, 91));
                graphics.FillPath(brush, drop);
                return icon;
            }
            var position = key switch
            {
                "triple" => new Point(32, 16),
                "quadra" => new Point(0, 0),
                "penta" => new Point(16, 32),
                "assists" => new Point(16, 0),
                "gold" => new Point(48, 0),
                "damage" => new Point(16, 16),
                "taken" => new Point(0, 16),
                "cs" => new Point(48, 32),
                "towers" => new Point(48, 16),
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };
            lock (Sprite)
            {
                return Sprite.Value.Clone(
                    new Rectangle(position, new Size(16, 16)),
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb
                );
            }
        }

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
