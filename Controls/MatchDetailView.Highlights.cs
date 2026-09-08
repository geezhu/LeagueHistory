using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchDetailView
{
    private void AddMatchHighlights(MatchInfo match)
    {
        var strip = new FlowLayoutPanel { Name = "MatchHighlights", Height = 86, WrapContents = false, BackColor = Color.White };
        foreach (var (field, title) in new[] { ("totalDamageDealtToChampions","最高英雄伤害"), ("totalDamageTaken","最高承受伤害"), ("largestKillingSpree","最多连杀") })
        {
            if (match.AllParticipants.Any(p => p[field]?.Type != JTokenType.Integer)) continue;
            int maximum = match.AllParticipants.Max(p => (int)p[field]!);
            var leaders = match.AllParticipants.Where(p => (int)p[field]! == maximum).ToArray();
            string name = leaders[0]["riotIdGameName"]?.ToString() ?? "玩家";
            var label = new Label { Text = $"{title}\n{maximum:N0}\n{name}{(leaders.Length > 1 ? " 等（并列）" : "")}",
                Width = 210, Height = 78, Padding = new Padding(12,6,0,0), ForeColor = Color.FromArgb(36,63,98) };
            strip.Controls.Add(label);
        }
        _scoreboard.Controls.Add(strip);
        ReflowScoreboard();
    }
}
