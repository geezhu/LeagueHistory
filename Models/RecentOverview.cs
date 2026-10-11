namespace League.Models;

public sealed record ModeWinRate(int Queue, string Name, int Games, int Wins)
{
    public double? Rate => Games > 0 ? Wins / (double)Games : null;
}

public sealed class RecentOverview
{
    public int Sample { get; private init; }
    public int KdaGames { get; private init; }
    public bool UsesAramKda { get; private init; }
    public double? Kda { get; private init; }
    public ModeWinRate[] Modes { get; private init; } = Array.Empty<ModeWinRate>();

    public static bool IsMatchmadeRift(int queue) => queue is 400 or 430 or 480 or 490;

    public static RecentOverview Build(IEnumerable<CareerGame> source)
    {
        var games = source
            .OrderByDescending(g => g.Created)
            .ThenByDescending(g => g.Id)
            .DistinctBy(g => g.Id)
            .Take(50)
            .ToArray();
        int Mode(CareerGame game) => IsMatchmadeRift(game.Queue) ? 430 : game.Queue;
        ModeWinRate Make(int queue) =>
            new(
                queue,
                queue == 430 ? "经典对战" : CareerStatistics.QueueName(queue),
                games.Count(g => Mode(g) == queue),
                games.Count(g => Mode(g) == queue && g.Win)
            );
        var modes = new[] { 430, 450, 420, 440 }.Select(Make).ToArray();
        var extras = games
            .Select(Mode)
            .Distinct()
            .Where(q => !modes.Any(m => m.Queue == q))
            .Select(Make)
            .OrderByDescending(m => m.Queue == 2400)
            .ThenByDescending(m => m.Games)
            .ThenBy(m => m.Queue);
        foreach (var extra in extras)
        {
            int replace = extra.Queue == 2400 ? Array.FindIndex(modes, m => m.Queue == 450 && m.Games == 0) : -1;
            if (replace < 0)
            {
                replace = Array.FindIndex(modes, m => m.Games == 0);
            }
            // Hextech ARAM must remain visible even when all four default modes have games.
            if (replace < 0 && extra.Queue == 2400)
            {
                replace = Enumerable.Range(0, 4).OrderBy(i => modes[i].Games).ThenByDescending(i => i).First();
            }

            if (replace >= 0)
            {
                modes[replace] = extra;
            }
        }
        var kda = games.Where(g => IsMatchmadeRift(g.Queue) || g.Queue is 420 or 440).ToArray();
        bool usesAram = false;
        if (kda.Length == 0)
        {
            kda = games.Where(g => g.Queue is 450 or 2400).ToArray();
            usesAram = kda.Length > 0;
        }
        return new RecentOverview
        {
            Sample = games.Length,
            Modes = modes,
            KdaGames = kda.Length,
            UsesAramKda = usesAram,
            Kda =
                kda.Length > 0 && kda.All(g => g.Kills.HasValue && g.Deaths.HasValue && g.Assists.HasValue)
                    ? 3d
                        * kda.Sum(g => (long)g.Kills!.Value + g.Assists!.Value)
                        / Math.Max(1L, kda.Sum(g => (long)g.Deaths!.Value))
                    : null,
        };
    }
}
