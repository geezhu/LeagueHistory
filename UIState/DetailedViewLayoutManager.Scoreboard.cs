using League.Models;
using Newtonsoft.Json.Linq;

namespace League.UIState
{
    public partial class DetailedViewLayoutManager
    {
        private void CreateScoreboardRow(Panel row, JObject player, string name, bool isSelf,
            (int damage, int damageRank, int gold, int goldRank, double kp, int kpRank, int damageTaken, int damageTakenRank) stats,
            MatchInfo match)
        {
            Color ink = Color.FromArgb(36, 48, 65), muted = Color.FromArgb(111, 123, 139);
            Label Text(string text, int x, int width, Color color, bool bold = false)
            {
                var label = new Label { Text = text, Location = new Point(x, 4), Size = new Size(width, 30),
                    ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular) };
                row.Controls.Add(label);
                _toolTip.SetToolTip(label, text);
                return label;
            }
            void Icon(Image? image, string? hint, int x, int y, int size)
            {
                var picture = new PictureBox { Location = new Point(x,y), Size = new Size(size,size),
                    BackColor = Color.FromArgb(239,243,248), SizeMode = PictureBoxSizeMode.Zoom,
                    Image = image == null ? null : new Bitmap(image) };
                row.Controls.Add(picture);
                _toolTip.SetToolTip(picture, StripHtmlTags(hint ?? ""));
            }
            void Cached(Dictionary<int,(Image,string,string)> cache, int id, int x, int y, int size)
            {
                if (cache.TryGetValue(id, out var data)) Icon(data.Item1, data.Item2 + "\n" + data.Item3, x,y,size);
                else Icon(null, id > 0 ? "资源暂未加载" : "空槽位", x,y,size);
            }
            if (isSelf) row.Controls.Add(new Panel { BackColor = Color.FromArgb(46,103,190), Size = new Size(3,44) });
            Cached(_championCache, player["championId"]?.Value<int>() ?? 0, 12,5,34);
            var nameLabel = Text(name, 58,180,isSelf ? Color.FromArgb(46,103,190) : ink, isSelf);
            var badges = League.UIHelpers.WeGameHonorIcons.Select(player, match.AllParticipants);
            bool firstBlood = player["firstBloodKill"]?.Value<bool>() == true;
            if (firstBlood || badges.Count > 0)
            {
                nameLabel.SetBounds(58, 1, 180, 22);
                int badgeX = 58;
                if (firstBlood)
                {
                    var first = new Label { Text = "首杀", Location = new Point(badgeX,25), Size = new Size(32,18),
                        ForeColor = Color.FromArgb(191,81,91), BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI",8f) };
                    row.Controls.Add(first); _toolTip.SetToolTip(first,"获得本场首杀"); badgeX += 36;
                }
                foreach (var badge in badges)
                {
                    var picture = new PictureBox { Name = "Honor_" + badge.Key, Location = new Point(badgeX,25), Size = new Size(16,16),
                        Image = League.UIHelpers.WeGameHonorIcons.Get(badge.Key), SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Transparent };
                    row.Controls.Add(picture); _toolTip.SetToolTip(picture,badge.Hint); badgeX += 20;
                }
            }
            for (int i=1;i<=2;i++) Cached(_spellCache,player[$"spell{i}Id"]?.Value<int>() ?? 0,242,11+(i-1)*12,10);
            for (int i=0;i<7;i++) Cached(_itemCache,player[$"item{i}"]?.Value<int>() ?? 0,258+i*24,11,22);
            if (IsHextechArenaMode(player))
            {
                var augments = GetSelfAugments(player,match);
                for (int i=0;i<Math.Min(6,augments.Length);i++)
                    Icon(augments[i]?.Icon, augments[i]?.name + "\n" + augments[i]?.longDesc,436+i*24,11,22);
            }
            else
            {
                var runes=LoadRunesForParticipant(player);
                var all=(runes.Primary ?? Array.Empty<RuneInfo>()).Concat(runes.Secondary ?? Array.Empty<RuneInfo>()).ToArray();
                for (int i=0;i<Math.Min(6,all.Length);i++) Cached(_runeCache,all[i].RuneId,436+i*24,11,22);
                // Retain access to the stat shards without expanding every row.
                string shards=string.Join("\n",(runes.Shards ?? Array.Empty<RuneInfo>()).Select(r =>
                    _runeCache.TryGetValue(r.RuneId,out var data) ? data.Item2 + ": " + StripHtmlTags(data.Item3) : "属性碎片 " + r.RuneId));
                if (shards.Length>0) _toolTip.SetToolTip(row,shards);
            }
            Text($"{player["kills"]} / {player["deaths"]} / {player["assists"]}",592,90,ink,true);
            void Metric(string key,int value,int rank,int x,Color color)
            {
                int maximum=match.AllParticipants.Max(p=>p[key]?.Value<int>() ?? 0);
                var label=Text($"{value/1000d:0.0}k",x,86,ink);
                _toolTip.SetToolTip(label,$"{value:N0} · 全场第 {rank} 名");
                var bar=new Panel { Location=new Point(x,36),Size=new Size(76,3),BackColor=Color.FromArgb(231,237,245) };
                int filled=maximum>0 && value>0 ? Math.Max(1,(int)(76d*value/maximum)) : 0;
                bar.Paint += (_,e) => { using var brush=new SolidBrush(color); e.Graphics.FillRectangle(brush,0,0,filled,3); };
                row.Controls.Add(bar);
            }
            Metric("goldEarned",stats.gold,stats.goldRank,686,Color.FromArgb(190,166,113));
            Metric("totalDamageDealtToChampions",stats.damage,stats.damageRank,780,Color.FromArgb(77,184,171));
            Text($"{stats.kp:P1}",880,70,muted);
            Text($"{(player["totalMinionsKilled"]?.Value<int>() ?? 0)+(player["neutralMinionsKilled"]?.Value<int>() ?? 0)}",958,60,muted);
            var taken=Text($"{stats.damageTaken/1000d:0.0}k",1026,90,muted);
            _toolTip.SetToolTip(taken,$"承受伤害 {stats.damageTaken:N0} · 全场第 {stats.damageTakenRank} 名");
        }
    }
}
