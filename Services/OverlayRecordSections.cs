using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using League.Models;
using Newtonsoft.Json.Linq;
using static League.Services.DetailCards;

namespace League.Services;

/// <summary>
/// The detail page's record tab: recent results, ranks, behavior tags and recent games from the main window's cached
/// history. It only reads the cache; the widget never causes a request of its own. Sections carry a layout the widget
/// draws as stat tiles, tag chips and game cards; explanations travel as tooltips.
/// </summary>
public static class OverlayRecordSections
{
    public const string Tab = "record";

    private const int MaxGames = 20;

    // The thresholds of MatchQueryProcessor.GenerateBehaviorSummary, upstream code that keeps them private.
    // RecordChecks feeds that method behaviors that produce every tag and compares the names with this list.
    public static readonly IReadOnlyDictionary<string, string> TagRules = new Dictionary<string, string>
    {
        ["爱投降"] = "以投降结束的对局 ≥ 38%",
        ["容易投降"] = "以投降结束的对局 ≥ 25%",
        ["爱发问号"] = "每场平均问号（敌人消失）≥ 3 次",
        ["爱发提醒"] = "每场平均视野提醒 ≥ 2.5 次",
        ["爱发撤退"] = "每场平均撤退 ≥ 3.5 次",
        ["爱发危险"] = "每场平均危险 ≥ 2.5 次",
        ["玻璃心"] = "四种信号每场合计 ≥ 13 次，或撤退 ≥ 4 次",
        ["心态容易崩"] = "四种信号每场合计 ≥ 9 次",
        ["爱抱怨"] = "撤退 ≥ 4.5 次且四种信号合计 ≥ 11 次",
    };

    // Severity follows upstream's README ratings: very high is "bad", high "warn", medium "info".
    private static readonly HashSet<string> SevereTags = ["爱投降", "玻璃心", "爱抱怨"];
    private static readonly HashSet<string> NotableTags = ["容易投降", "爱发问号", "爱发撤退", "心态容易崩"];

    public static JArray Build(
        LoadingPlayer player,
        PlayerMatchInfo? info,
        Func<int, string> champion,
        bool icons = false
    )
    {
        if (player.IdentityHidden)
        {
            return [Notice("隐藏玩家", "该玩家隐藏了名称，无法对应到战绩。")];
        }

        if (info == null)
        {
            return
            [
                Notice(
                    "暂无战绩",
                    "主程序还没有这名玩家的战绩：选人或加载时主窗口查询过的玩家才有。读取完成后点「重试」；对局中途启动 League 时只有自己。"
                ),
            ];
        }

        int games = Math.Min(MaxGames, Math.Min(info.WinHistory.Count, info.RecentMatches.Count));
        if (games == 0)
        {
            return
            [
                Notice(
                    "暂无战绩",
                    info.Player.IsPublic == "隐藏" ? "该玩家的战绩设为隐藏。" : "最近没有这个模式的对局。"
                ),
            ];
        }

        var results = Enumerable.Range(0, games).Select(i => Result(info, i)).ToArray();
        return [Summary(info, games, results, champion), Behavior(info), Games(info, games, results, champion, icons)];
    }

    /// <summary>The behavior tags upstream wrote as "[a、b]"; empty when none applies.</summary>
    public static string[] Tags(PlayerBehavior? behavior) =>
        (behavior?.BehaviorSummary ?? "")
            .Trim('[', ']', ' ')
            .Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JObject Summary(PlayerMatchInfo info, int games, string[] results, Func<int, string> champion)
    {
        int remakes = results.Count(r => r == "remake");
        int wins = results.Count(r => r == "win");
        int losses = games - wins - remakes;
        var stats = info.RecentMatches.Take(games).ToArray();
        double kills = stats.Average(m => m.Kills),
            deaths = stats.Average(m => m.Deaths),
            assists = stats.Average(m => m.Assists);
        double rate = wins * 100.0 / games;
        var (streak, streakTone) = Streak(results);
        var tiles = new List<JObject>
        {
            Tile(
                "胜率",
                Percent(rate),
                $"{wins} 胜 {losses} 负" + (remakes > 0 ? $" {remakes} 重开" : ""),
                rate >= 55 ? "good"
                    : rate <= 45 ? "bad"
                    : "",
                "胜率按全部场次计算（含重开），与发送的战绩一致。"
            ),
            Tile("近况", streak, "左侧色块为最新", streakTone, "重开不计入连胜连败。"),
            Tile(
                "平均 KDA",
                $"{kills:0.0}/{deaths:0.0}/{assists:0.0}",
                $"KDA {(kills + assists) / Math.Max(1, deaths):0.0}"
            ),
            Rank("单双排", info.Player.SoloRank),
            Rank("灵活组排", info.Player.FlexRank),
        };
        var section = Section(Tab, "近期战绩", Tiles, tiles);
        section["tip"] =
            $"主窗口查询的最近 {games} 场，只读主程序缓存，不额外请求。"
            + (info.Player.IsPublic == "隐藏" ? "\n该玩家的资料设为隐藏。" : "");
        section["form"] = new JArray(
            results.Select(
                (result, i) =>
                    new JObject
                    {
                        ["result"] = result,
                        ["description"] = Label(result) + " · " + ChampionName(info, Item(info, i), champion),
                    }
            )
        );
        return section;
    }

    private static (string Text, string Tone) Streak(string[] results)
    {
        // Matches arrive newest first; a remake neither extends nor breaks a streak.
        var played = results.Where(r => r != "remake").ToArray();
        if (played.Length == 0)
        {
            return ("暂无", "");
        }

        int count = played.TakeWhile(r => r == played[0]).Count();
        bool win = played[0] == "win";
        string text =
            count >= 2 ? $"{count} 连{(win ? "胜" : "败")}"
            : win ? "上一场胜"
            : "上一场负";
        return (text, win ? "good" : "bad");
    }

    private static JObject Rank(string queue, string? rank)
    {
        // MatchQueryProcessor formats ranks as "黄金 II(45)" and unknown ones as "未知".
        var match = Regex.Match(rank ?? "", @"^(.*)\((\d+)\)$");
        return match.Success
            ? Tile(queue, match.Groups[1].Value.Trim(), match.Groups[2].Value + " 胜点")
            : Tile(queue, string.IsNullOrWhiteSpace(rank) ? "未知" : rank, "");
    }

    private static JObject Behavior(PlayerMatchInfo info)
    {
        var b = info.Behavior ?? new PlayerBehavior();
        var chips = Tags(b)
            .Select(tag =>
                Tile(
                    tag,
                    "",
                    "",
                    SevereTags.Contains(tag) ? "bad"
                        : NotableTags.Contains(tag) ? "warn"
                        : "info",
                    TagRules.GetValueOrDefault(tag, "规则未说明")
                )
            )
            .ToList();
        double total = b.AvgMissingPings + b.AvgVisionPings + b.AvgGetBackPings + b.AvgDangerPings;
        var section = Section(Tab, "行为特征", Chips, chips);
        section["tip"] =
            $"按主窗口查询的最近 {info.RecentMatches.Count} 场估算，仅供参考。\n投降率统计以投降结束的对局，不区分是哪一方投降；信号为每场平均次数。";
        section["signals"] = new JArray(
            Tile(
                "投降率",
                Percent(b.SurrenderRate),
                $"短局 {b.ShortGameCount} 场",
                Level(b.SurrenderRate, 25, 38),
                "以投降结束的对局占比（不区分哪一方投降）。≥ 25% 容易投降，≥ 38% 爱投降。\n短局：18 分钟内结束的对局。"
            ),
            Tile(
                "问号",
                $"{b.AvgMissingPings:0.0}",
                "次 / 场",
                Level(b.AvgMissingPings, 3),
                "问号（敌人消失）≥ 3 次为爱发问号。"
            ),
            Tile(
                "撤退",
                $"{b.AvgGetBackPings:0.0}",
                "次 / 场",
                Level(b.AvgGetBackPings, 3.5, 4),
                "≥ 3.5 次为爱发撤退，≥ 4 次计为玻璃心。"
            ),
            Tile("危险", $"{b.AvgDangerPings:0.0}", "次 / 场", Level(b.AvgDangerPings, 2.5), "≥ 2.5 次为爱发危险。"),
            Tile(
                "提醒",
                $"{b.AvgVisionPings:0.0}",
                "次 / 场",
                b.AvgVisionPings >= 2.5 ? "info" : "",
                "视野提醒 ≥ 2.5 次为爱发提醒，属于视野意识较强。"
            ),
            Tile(
                "信号合计",
                $"{total:0.0}",
                "次 / 场",
                Level(total, 9, 13),
                "四种信号合计：≥ 9 次心态容易崩，≥ 13 次玻璃心。"
            )
        );
        return section;
    }

    private static string Level(double value, double warn, double bad = double.PositiveInfinity) =>
        value >= bad ? "bad"
        : value >= warn ? "warn"
        : "";

    private static JObject Games(
        PlayerMatchInfo info,
        int games,
        string[] results,
        Func<int, string> champion,
        bool icons
    )
    {
        var images = new Dictionary<string, string>();
        var rows = new List<JObject>();
        for (int i = 0; i < games; i++)
        {
            var item = Item(info, i);
            var stat = info.RecentMatches[i];
            string mode = item?.SubItems.Count > 1 ? item.SubItems[1].Text : "";
            string date = item?.SubItems.Count > 3 ? item.SubItems[3].Text : "";
            string name = ChampionName(info, item, champion);
            // The history is queried for the current mode, so the card leaves the mode to the tooltip.
            var facts = new[]
            {
                stat.GameDurationMinutes > 0 ? $"{stat.GameDurationMinutes} 分钟" : "",
                date.Length == 10 ? date[5..] : date,
            }.Where(p => p.Length > 0);
            bool surrendered = stat.GameEndedInSurrender && results[i] != "remake";
            var row = Tile(
                name,
                $"{stat.Kills}/{stat.Deaths}/{stat.Assists}",
                string.Join(" · ", facts),
                results[i],
                $"{Label(results[i])} · {name} · {stat.Kills}/{stat.Deaths}/{stat.Assists}"
                    + (mode.Length > 0 ? " · " + mode : "")
                    + (date.Length > 0 ? " · " + date : "")
                    + (surrendered ? " · 投降结束" : "")
            );
            row["result"] = Label(results[i]);
            row["badge"] = surrendered ? "投降" : "";
            row["image"] = icons && item != null ? Icon(info, item.ImageKey, images) : "";
            rows.Add(row);
        }

        return Section(Tab, "最近对局", DetailCards.Games, rows);
    }

    private static string Result(PlayerMatchInfo info, int index) =>
        Remake(Item(info, index)) ? "remake"
        : info.WinHistory[index] ? "win"
        : "loss";

    private static string Label(string result) =>
        result switch
        {
            "win" => "胜",
            "loss" => "负",
            _ => "重开",
        };

    private static ListViewItem? Item(PlayerMatchInfo info, int index) => info.MatchItems.ElementAtOrDefault(index);

    private static string ChampionName(PlayerMatchInfo info, ListViewItem? item, Func<int, string> champion)
    {
        var participant = ((item?.Tag as MatchMetadata)?.GameData?["participants"] as JArray)
            ?.OfType<JObject>()
            .FirstOrDefault(p => (string?)p["puuid"] == info.Player.Puuid);
        int id = (int?)participant?["championId"] ?? 0;
        return id > 0 ? champion(id) : item?.ImageKey ?? "未知英雄";
    }

    // The history parser already loaded each game's champion into the player's image list; the widget falls back to
    // the name's first character without one. Reading an image list needs its window handle, so only the UI thread
    // asks for icons.
    private static string Icon(PlayerMatchInfo info, string key, Dictionary<string, string> encoded)
    {
        if (encoded.TryGetValue(key, out var cached))
        {
            return cached;
        }

        string value = "";
        if (info.HeroIcons?.Images.ContainsKey(key) == true)
        {
            using var image = info.HeroIcons.Images[key]!;
            using var buffer = new MemoryStream();
            image.Save(buffer, ImageFormat.Png);
            value = Convert.ToBase64String(buffer.ToArray());
        }

        encoded[key] = value;
        return value;
    }

    // The history list colors remakes; the parser decides it from the early-surrender flags.
    private static bool Remake(ListViewItem? item) => item?.ForeColor == MatchResult.RemakeColor;

    private static string Percent(double value) => value.ToString("0", CultureInfo.InvariantCulture) + "%";

    private static JObject Notice(string title, string description) =>
        OverlayChampionDetailService.Section(Tab, "近期战绩", [OverlayChampionDetailService.Row(title, description)]);
}
