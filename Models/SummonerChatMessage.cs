using League.Services;

namespace League.Models;

/// <summary>
/// One ally summoner spell's timer as a chat line, sent when the user right-clicks the spell on the card. The line
/// names only the spell; the user shows whose it is by clicking the champion on the Tab scoreboard. Enemies' spells
/// are never timed or sent: Riot's rules forbid tracking enemy summoner cooldowns.
/// </summary>
public static class SummonerChatMessage
{
    private const long StaleMilliseconds = 15000;

    public static IReadOnlyList<string> Build(
        LoadingPlayer? player,
        int ownTeam,
        OverlayLiveState? live,
        int spell,
        long now
    )
    {
        if (player == null)
        {
            throw new InvalidOperationException("未发送：该位置的英雄已变化。");
        }

        if (ownTeam == 0 || player.Team != ownTeam)
        {
            throw new InvalidOperationException("未发送：只能发送我方的召唤师技能计时。");
        }

        if (live == null || now < live.GameTimeAt || now - live.GameTimeAt > StaleMilliseconds)
        {
            throw new InvalidOperationException("未发送：对局数据未更新，请等待 2999 连接恢复。");
        }

        var summoner = live.Summoners.GetValueOrDefault(player.Key)?.FirstOrDefault(s => s.Slot == spell);
        if (summoner?.ReadyAt is not double readyAt || summoner.Cooldown <= 0)
        {
            throw new InvalidOperationException("未发送：先左键点击技能图标开始计时。");
        }

        double time = live.GameTime + (now - live.GameTimeAt) / 1000.0;
        string name = summoner.Spell.Name;
        return
        [
            readyAt > time
                ? $"召唤师技能：{name} 还有 {MinionWaveForecaster.Clock(readyAt - time)}，约 {MinionWaveForecaster.Clock(readyAt)} 转好"
                : $"召唤师技能：{name} 已转好",
        ];
    }
}
