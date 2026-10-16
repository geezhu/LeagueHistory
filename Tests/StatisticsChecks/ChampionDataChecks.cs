using System.Reflection;
using League.Models;
using League.PrimaryElection;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The champion list the assistant and the preselect lists read, and the team summary the chat button builds from
/// it. The list is filled directly, so nothing is downloaded and no client is needed.
/// </summary>
internal static class ChampionDataChecks
{
    public static void Run()
    {
        ChampionLookups();
        TeamSummaries();
        Console.WriteLine(
            "PASS: champion lookup by id and lane, and the chat team summary's win rate, ping details, "
                + "champion names and self hiding."
        );
    }

    private static void ChampionLookups()
    {
        var manager = Manager(
            Champion(1, "腕豪", active: true),
            Champion(2, "武器大师", active: true),
            Champion(3, "暗夜猎手", active: true),
            Champion(4, "已下架英雄", active: false)
        );

        Check(manager.GetChampionById(3)?.Name == "暗夜猎手", "A champion is found by id");
        Check(manager.GetChampionById(999) == null, "An unknown champion id finds nothing");

        Check(
            manager.GetChampionsByPosition(ChampionPosition.Jungle).Select(c => c.Name).SequenceEqual(["武器大师"]),
            "The jungle list holds only the champions played there"
        );
        var top = manager.GetChampionsByPosition(ChampionPosition.Top);
        Check(
            top.Select(c => c.Name).ToHashSet().SetEquals(["腕豪", "武器大师", "暗夜猎手"]),
            "The top list holds every champion played there"
        );
        var laneNames = top.Select(c => c.Name).ToArray();
        Check(laneNames.SequenceEqual(laneNames.OrderBy(n => n)), "A lane list comes back in name order");
        Check(
            manager.GetChampionsByPosition(ChampionPosition.All).Count == 3,
            "The full list leaves out champions that are no longer active"
        );
        Check(
            manager.GetChampionsByPosition(ChampionPosition.Mid).Count == 0,
            "A lane nobody in the list plays is empty"
        );
        Check(
            manager.GetChampionsByPosition(ChampionPosition.Support).Select(c => c.Name).SequenceEqual(["腕豪"]),
            "A champion played in two lanes appears in both"
        );
    }

    private static void TeamSummaries()
    {
        SummaryLines();
        SummaryAudience();
    }

    private static void SummaryLines()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        var builder = Builder(true, player);
        var team = new JArray(Player(7, "puuid-a", 0));

        Check(
            builder.BuildMyTeamSummary(team) == "玩家甲 近4场 胜率 [75%]",
            "The summary gives the player, the number of games and the win rate"
        );

        player.Behavior = new PlayerBehavior
        {
            BehaviorSummary = "稳健",
            AvgMissingPings = 2.5,
            AvgVisionPings = 1.5,
        };
        Check(
            builder.BuildMyTeamSummary(team) == "玩家甲 近4场 胜率 [75%] | 发问号 2.5 | 发提醒 1.5 特征 [稳健]",
            "The summary adds the ping averages it was given"
        );

        player.WinHistory = [];
        Check(builder.BuildMyTeamSummary(team) == "玩家甲 暂无战绩", "A player without games says so");

        player.WinHistory = [true, false];
        player.Behavior = new PlayerBehavior(); // Only the name is under test from here on.
        Check(
            builder.BuildMyTeamSummary(new JArray(Player(7, "puuid-a", 42)), useChampionName: true)
                == "迅捷斥候 近2场 胜率 [50%]",
            "With champion names the summary shows the champion instead of the player"
        );
        Check(
            builder.BuildMyTeamSummary(new JArray(Player(7, "puuid-a", 999)), useChampionName: true)
                == "玩家甲 近2场 胜率 [50%]",
            "An unknown champion id falls back to the player name"
        );
        Check(
            builder.BuildMyTeamSummary(new JArray(Player(7, "puuid-a", 0)), useChampionName: true)
                == "玩家甲 近2场 胜率 [50%]",
            "A player without a champion falls back to the player name"
        );
    }

    private static void SummaryAudience()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];

        var builder = Builder(true, player);
        builder.SetMyPuuid("puuid-self");
        Check(
            builder.BuildMyTeamSummary(new JArray(Player(7, "puuid-self", 0))) == "",
            "The sender's own line is left out"
        );

        var withSelf = Builder(false, player);
        withSelf.SetMyPuuid("puuid-self");
        Check(
            withSelf
                .BuildMyTeamSummary(new JArray(Player(7, "puuid-self", 0)))
                .StartsWith("玩家甲", StringComparison.Ordinal),
            "A summary that should show everyone keeps the sender"
        );

        var full = builder.BuildFullTeamSummary(
            new JArray(Player(7, "puuid-a", 0)),
            new JArray(Player(8, "puuid-b", 0))
        );
        Check(full.Contains("玩家甲", StringComparison.Ordinal), "The full summary lists our team");
        Check(!full.Contains("玩家乙", StringComparison.Ordinal), "A player with no cached games is left out");

        var both = Builder(true, player, Info(8, "玩家乙"));
        var bothTeams = both.BuildFullTeamSummary(
            new JArray(Player(7, "puuid-a", 0)),
            new JArray(Player(8, "puuid-b", 0))
        );
        var lines = bothTeams.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        Check(
            lines.Length == 3
                && lines[0].StartsWith("玩家甲", StringComparison.Ordinal)
                && lines[1].Length == 0
                && lines[2].StartsWith("玩家乙", StringComparison.Ordinal),
            "Our team and the enemy team are separated by a blank line"
        );

        Check(builder.BuildMyTeamSummary(null) == "暂无数据", "A missing team says there is no data");
        Check(builder.BuildMyTeamSummary(new JArray()) == "暂无数据", "An empty team says there is no data");
        Check(builder.BuildMyTeamSummary(new JArray(Player(7, "", 0))) == "", "A player without a puuid is skipped");
        Check(
            builder.BuildMyTeamSummary(new JArray(Player(0, "puuid-c", 0))) == "",
            "A player without a summoner is skipped"
        );
    }

    private static ChatMessageBuilder Builder(bool hideSelf, params PlayerMatchInfo[] players)
    {
        var known = players.ToDictionary(player => player.Player.SummonerId);
        return new ChatMessageBuilder(() => known, Manager(Champion(42, "迅捷斥候", active: true)), hideSelf);
    }

    private static PrimaryChampion Champion(int id, string name, bool active) =>
        new()
        {
            Id = id,
            Name = name,
            Alias = name,
            IsActive = active,
        };

    private static JObject Player(long summonerId, string puuid, int championId) =>
        new()
        {
            ["summonerId"] = summonerId,
            ["puuid"] = puuid,
            ["championId"] = championId,
        };

    private static PlayerMatchInfo Info(long summonerId, string name) =>
        new()
        {
            Player = new PlayerInfo
            {
                SummonerId = summonerId,
                GameName = name,
                Puuid = $"puuid-{summonerId}",
            },
        };

    /// <summary>A champion manager holding only the given champions; nothing is loaded or downloaded.</summary>
    private static ChampionManager Manager(params PrimaryChampion[] champions)
    {
        var manager = new ChampionManager(new HttpClient(), new League.Infrastructure.ResourceLoading());
        ChampionsField.SetValue(manager, champions.ToList());
        return manager;
    }

    private static readonly FieldInfo ChampionsField = typeof(ChampionManager).GetField(
        "_allChampions",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;
}
