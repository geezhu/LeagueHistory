using League;
using League.Infrastructure;
using League.Managers;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The player card factory: the placeholder cards for a player still loading, for a hidden player and for one whose
/// lookup failed, including the fallback avatar. The shared resource cache is swapped for one holding a single
/// champion, so no client, network or real asset folder is involved.
/// </summary>
internal static class PlayerCardFactoryChecks
{
    public static void Run()
    {
        var previous = FormMain.Globals.resLoading;
        try
        {
            FormMain.Globals.resLoading = Resources();
            var factory = new PlayerCardFactory();

            Loading(factory);
            Hidden(factory);
            Failed(factory);
        }
        finally
        {
            FormMain.Globals.resLoading = previous;
        }

        Console.WriteLine(
            "PASS: the player card factory builds the loading, hidden and failed placeholder cards, with the shared "
                + "champion list and the fallback avatar."
        );
    }

    private static void Loading(PlayerCardFactory factory)
    {
        var player = factory.CreateLoadingPlayerInfo(JObject.Parse("""{"summonerId":123,"championId":99}"""));
        Check(player.Player.SummonerId == 123 && player.Player.ChampionId == 99, "A public player keeps its ids");
        Check(player.Player.ChampionName == "A3测试英雄", "The champion name comes from the shared champion list");
        Check(
            player.Player.GameName == "加载中..."
                && player.Player.FullName == "加载中..."
                && player.Player.IsPublic == "[查询中]",
            "A public player shows the pending placeholders"
        );
        Check(
            player.Player.SoloRank == "加载中..."
                && player.Player.FlexRank == "加载中..."
                && player.Player.NameColor == Color.White,
            "A public player is drawn in white while its ranks load"
        );
        Check(
            player.MatchItems.Count == 0
                && player.HeroIcons != null
                && (player.Player.Avatar != null) == HasDefaultAvatar(),
            "A card owns its own lists and the fallback avatar when the app ships one"
        );

        var hidden = factory.CreateLoadingPlayerInfo(new JObject());
        Check(
            hidden.Player.SummonerId == 0
                && hidden.Player.GameName == "隐藏玩家"
                && hidden.Player.NameColor == Color.Gray,
            "A player without a summoner id is hidden"
        );
        Check(
            hidden.Player.SoloRank == "隐藏" && hidden.Player.FlexRank == "隐藏" && hidden.Player.IsPublic == "隐藏",
            "A hidden player reports no ranks"
        );
        Check(hidden.Player.ChampionName == "Unknown", "A champion the shared list does not know is named as unknown");
    }

    private static void Hidden(PlayerCardFactory factory)
    {
        var hidden = factory.CreateHiddenPlayerInfo(7, 99);
        Check(hidden.Player.SummonerId == 7 && hidden.Player.ChampionId == 99, "A hidden player keeps its ids");
        Check(hidden.Player.ChampionName == "A3测试英雄", "A hidden player still names its champion");
        Check(
            hidden.Player.GameName == "隐藏玩家"
                && hidden.Player.IsPublic == "隐藏"
                && hidden.Player.NameColor == Color.Gray,
            "A hidden player is drawn grey"
        );
        Check((hidden.Player.Avatar != null) == HasDefaultAvatar(), "A hidden player gets the fallback avatar");
        Check(
            factory.CreateHiddenPlayerInfo(7, 0).Player.ChampionName == "未知英雄",
            "A hidden player without a champion says the champion is unknown"
        );
    }

    private static void Failed(PlayerCardFactory factory)
    {
        var failed = factory.CreateFailedPlayerInfo(7, 99, "puuid-1");
        Check(
            failed.Player.SummonerId == 7 && failed.Player.Puuid == "puuid-1",
            "A failed lookup keeps the id and the puuid"
        );
        Check(
            failed.Player.ChampionName == "查询失败"
                && failed.Player.GameName == "失败"
                && failed.Player.IsPublic == "[失败]",
            "A failed lookup says so on the card"
        );
        Check(
            failed.Player.SoloRank == "失败"
                && failed.Player.FlexRank == "失败"
                && failed.Player.NameColor == Color.DarkRed,
            "A failed lookup is drawn dark red"
        );
        Check((failed.Player.Avatar != null) == HasDefaultAvatar(), "A failed lookup gets the fallback avatar");

        var anonymous = factory.CreateFailedPlayerInfo(0, 99);
        Check(
            anonymous.Player.GameName == "隐藏玩家" && anonymous.Player.NameColor == Color.Gray,
            "A failed lookup without a summoner id falls back to the hidden card"
        );
    }

    private static bool HasDefaultAvatar() =>
        File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Defaults", "Profile.png"));

    /// <summary>A resource cache holding one champion; the loaders it ships with are never asked to download.</summary>
    private static ResourceLoading Resources()
    {
        var resources = new ResourceLoading();
        resources.Champions.Champions.Add(
            new Champion
            {
                Id = 99,
                Name = "A3测试英雄",
                Alias = "A3Lux",
                Description = string.Empty,
            }
        );
        return resources;
    }
}
