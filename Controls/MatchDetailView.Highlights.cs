using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchDetailView
{
    private void AddMatchHighlights(MatchInfo match)
    {
        if (match.AllParticipants.Count == 0)
        {
            return;
        }
        var strip = new FlowLayoutPanel
        {
            Name = "MatchHighlights",
            Height = 86,
            WrapContents = false,
            BackColor = Color.White,
        };
        foreach (
            var (field, title) in new[]
            {
                ("totalDamageDealtToChampions", "最高英雄伤害"),
                ("totalDamageTaken", "最高承受伤害"),
                ("largestKillingSpree", "最多连杀"),
            }
        )
        {
            if (match.AllParticipants.Any(p => p[field]?.Type != JTokenType.Integer))
            {
                continue;
            }

            int maximum = match.AllParticipants.Max(p => (int)p[field]!);
            var leaders = match.AllParticipants.Where(p => (int)p[field]! == maximum).ToArray();
            strip.Controls.Add(
                new MatchHighlightCard(title, maximum, (int?)leaders[0]["championId"] ?? 0, leaders.Length > 1)
            );
        }
        _scoreboard.Controls.Add(strip);
        ReflowScoreboard();
    }
}
