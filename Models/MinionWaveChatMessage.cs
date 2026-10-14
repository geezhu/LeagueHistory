using League.Services;

namespace League.Models;

/// <summary>Team chat lines from the wave estimate, built at send time so countdowns match the moment of sending.</summary>
public static class MinionWaveChatMessage
{
    public const string Spawn = "spawn",
        Super = "super",
        Deaths = "deaths";

    private const long StaleMilliseconds = 15000;

    public static bool IsTarget(string target) => target is Spawn or Super or Deaths;

    public static IReadOnlyList<string> Build(MinionWaveForecast? forecast, string target, long now)
    {
        if (forecast == null || now - forecast.Clock > StaleMilliseconds || now < forecast.Clock)
        {
            throw new InvalidOperationException("未发送：兵线数据未更新，请等待 2999 连接恢复。");
        }

        double time = forecast.GameTime + (now - forecast.Clock) / 1000.0;
        return target switch
        {
            Spawn => [SpawnLine(forecast, time)],
            Super => SuperLines(forecast),
            Deaths => DeathLines(forecast),
            _ => throw new InvalidOperationException("未发送：未知的兵线内容。"),
        };
    }

    private static string SpawnLine(MinionWaveForecast forecast, double time)
    {
        var next = forecast.Spawns.FirstOrDefault(s => s.Time > time) ?? forecast.Spawns.Last();
        var cannon = forecast.Spawns.FirstOrDefault(s => s.Time > time && s.Siege > 0);
        string line =
            $"兵线：{MinionWaveForecaster.Clock(Math.Max(0, next.Time - time))} 后出第 {next.Number} 波"
            + (next.Siege > 0 ? "（炮车波）" : "");
        if (cannon != null && cannon != next)
        {
            line += $"；下个炮车波 {MinionWaveForecaster.Clock(cannon.Time)}";
        }
        return line + "；" + string.Join("，", forecast.Notes);
    }

    private static IReadOnlyList<string> SuperLines(MinionWaveForecast forecast)
    {
        var lines = forecast
            .Lanes.Where(l => l.Structures.Length > 0)
            .Select(l => (forecast.Lanes.Length > 1 ? l.Name + " " : "") + l.Structures)
            .ToArray();
        return lines.Length > 0
            ? lines.Select(l => "超级兵：" + l).ToArray()
            : throw new InvalidOperationException("未发送：目前没有被破的小水晶。");
    }

    private static IReadOnlyList<string> DeathLines(MinionWaveForecast forecast)
    {
        if (forecast.Deaths.Length == 0)
        {
            throw new InvalidOperationException("未发送：当前模式没有复活时间数据。");
        }

        string bonus = forecast.DeathFactor >= 0.5 ? $"（时间加成 +{forecast.DeathFactor:0}%）" : "";
        string Team(bool ally) =>
            string.Join(
                "，",
                forecast.Deaths.Where(d => d.Ally == ally).Select(d => $"{d.Champion} {d.Level}级{d.Seconds:0}秒")
            );
        // Enemies first: their downtime is what a fight or objective call depends on.
        return [$"现在阵亡需等待{bonus} 敌方：{Team(false)}", $"我方：{Team(true)}"];
    }
}
