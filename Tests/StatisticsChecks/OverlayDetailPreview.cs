using System.Drawing.Imaging;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class OverlayDetailPreview
{
    // Exports a separate, explicitly labelled fixture. Never touches live snapshot/request files.
    public static void Export(string destination, string assetDirectory)
    {
        Directory.CreateDirectory(destination);
        var content = CreateContent();
        PopulateIcons(content, assetDirectory);
        SetPreviewMetadata(content, assetDirectory);
        var heroIcon = Encode(Path.Combine(assetDirectory, "champion", "九尾妖狐.png"));
        var roster = CreateRoster(heroIcon);
        AddPlayerAnnotations(roster);
        AddLivePreview(roster);
        PopulateIcons(roster, assetDirectory);
        File.WriteAllText(Path.Combine(destination, "detail-preview-content.json"), content.ToString());
        File.WriteAllText(Path.Combine(destination, "detail-preview-snapshot.json"), roster.ToString());
        Console.WriteLine("Detail preview fixture exported to " + destination);
    }

    private static JObject CreateContent()
    {
        var index = AramMetaIndex.Parse(
            JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/AramMeta/index.json")))
        );
        var detail = JObject.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/AramMeta/103.json"))
        );
        var content = OverlayChampionDetailService.FromAramMeta(new(index, detail, true, DateTime.UtcNow), 103);
        var opgg = OpggBuild.Parse(
            JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/Opgg/aram.json"))),
            new("global", "aram", "all", "none", 103)
        );
        foreach (
            var section in ((JArray)OverlayChampionDetailService.FromOpgg(opgg)["sections"]!).Where(s =>
                (string?)s["tab"] == "runes"
            )
        )
        {
            ((JArray)content["sections"]!).Add(section.DeepClone());
        }
        ((JArray)content["sections"]!).Insert(
            0,
            OverlayChampionDetailService.Section(
                "skills",
                "技能说明 · 布局测试",
                new[] { "被动", "Q", "W", "E", "R" }.Select(key =>
                    OverlayChampionDetailService.Row(
                        key + " · 技能说明示例",
                        "这是一段用于检查换行的长说明。技能说明应随着面板宽度自然换行，不能越过卡片边缘，也不能遮挡下面的推荐内容。\n第二段用于检查多段文字的间距。",
                        [
                            new JObject
                            {
                                ["kind"] = "skill",
                                ["id"] = 21,
                                ["key"] = key,
                                ["name"] = "布局测试图标",
                                ["description"] = "悬浮说明应完整换行。",
                            },
                        ]
                    )
                )
            )
        );
        return content;
    }

    private static void PopulateIcons(JObject content, string assetDirectory)
    {
        foreach (var icon in content.Descendants().OfType<JObject>().Where(o => o["kind"] != null))
        {
            string kind = (string?)icon["kind"] ?? "";
            int id = (int?)icon["id"] ?? 0;
            string? path = null;
            if (kind == "skill")
            {
                path = Path.Combine(assetDirectory, "skills", $"{id}-{(string?)icon["key"]}.png");
            }
            else
            {
                string folder = Path.Combine(
                    assetDirectory,
                    kind switch
                    {
                        "item" => "item",
                        "augment" => "augments",
                        "rune" => "rune",
                        _ => "spell",
                    }
                );
                if (Directory.Exists(folder))
                {
                    path = Directory
                        .EnumerateFiles(folder)
                        .FirstOrDefault(f =>
                            Path.GetFileNameWithoutExtension(f) == id.ToString()
                            || Path.GetFileNameWithoutExtension(f).StartsWith(id + "_")
                            || Path.GetFileNameWithoutExtension(f).EndsWith("_" + id)
                        );
                }
            }
            if (path != null)
            {
                icon["image"] = Encode(path);
            }
        }
    }

    private static void SetPreviewMetadata(JObject content, string assetDirectory)
    {
        content["art"] = Encode(Path.Combine(assetDirectory, "profile-backgrounds", "21000.png"), 560, 280);
        content["revision"] = "fixture-1";
        content["state"] = "ready";
        content["mode"] = "布局测试";
        content["source"] = "本地测试样例 · 不代表当前对局";
    }

    private static JObject CreateRoster(string heroIcon)
    {
        var roster = new JObject
        {
            ["protocol"] = 1,
            ["detailProtocol"] = 1,
            ["chatProtocol"] = 1,
            ["chatMastery"] = true,
            ["chatDeathStreak"] = true,
            ["chatVoice"] = true,
            ["voiceProtocol"] = 2,
            ["chatTeams"] = true,
            ["queue"] = 2400,
            ["revision"] = "fixture-1",
            ["session"] = "fixture-session",
            ["command"] = "fixture-command",
            ["enabled"] = true,
            ["active"] = true,
            ["show"] = true,
            ["status"] = "英雄详情布局验证 · 本地样例",
            ["teams"] = new JArray("我方", "对方"),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["players"] = new JArray(
                Enumerable
                    .Range(0, 10)
                    .Select(i => new JObject
                    {
                        ["championId"] = 103,
                        ["champion"] = "样例英雄",
                        ["name"] = $"测试玩家 {i + 1}",
                        ["tier"] = "T2",
                        ["stats"] = "胜率 52.4% · 选用率 8.7%",
                        ["mastery"] = i == 0 ? "成就 12级 · 18.6万" : $"成就 {i + 1}级 · {(i + 1) * 1000:N0}",
                        ["masteryDetails"] = "英雄成就：12级 · 186,321 点\n最近使用：2026-09-30\n本地测试样例",
                        ["details"] = "缓存战绩示例",
                        ["icon"] = heroIcon,
                    })
            ),
        };
        return roster;
    }

    private static void AddPlayerAnnotations(JObject roster)
    {
        int slot = 0;
        foreach (var row in ((JArray)roster["players"]!).OfType<JObject>())
        {
            var streak =
                slot == 3
                    ? LiveDeathStreak.Unavailable("布局样例：隐藏玩家无法匹配事件。")
                    : new LiveDeathStreak(
                        slot % 4,
                        slot + 3,
                        slot != 2,
                        slot == 2 ? "* 布局样例：仅记录部分事件。" : "布局样例：完整本局事件。"
                    );
            row["deathStreak"] = streak.Text;
            row["deathStreakDetails"] = streak.Rule;
            row["currentDeathStreak"] = streak.Current;
            row["maxDeathStreak"] = streak.Highest;
            var party = new OverlayPartyMarker(
                slot < 5 ? 5
                    : slot < 7 ? 2
                    : 3,
                slot < 5 ? 0xff8d9e
                    : slot < 7 ? 0xcba2ff
                    : 0xffce7a
            );
            row["party"] = party.Badge;
            row["partySize"] = party.Size;
            row["partyColor"] = party.Color;
            row["partyLabel"] = party.Label;
            row["partyDetails"] = party.Description;
            slot++;
        }
    }

    // Scoreboard facts as 2999 would report them mid-game: two enemies dead, and the active player's panel.
    private static void AddLivePreview(JObject roster)
    {
        int slot = 0;
        foreach (var row in ((JArray)roster["players"]!).OfType<JObject>())
        {
            row["self"] = slot == 0;
            row["respawn"] = slot is 6 or 8 ? 9 + slot * 2 : 0;
            row["liveStats"] = $"参团 {40 + slot * 5}% · 装备 {5.2 + slot * 0.4:0.0}k";
            slot++;
        }

        var insight = new LiveMatchInsight(
            612,
            new(18, 41200, 1, 3, 3),
            new(14, 38900, 0, 1, 2),
            new Dictionary<string, LivePlayerInsight>(),
            [new(301, 3, 1), new(455, 1, 3), new(588, 4, 2)],
            ""
        );
        var stats = new LiveChampionStats(2380, 96, 58, 25, 12, 0.18, 0, 0);
        var self = new LivePlayerInsight(5200, 5, 2, 9, 0.78, 0);
        var input = new LivePanelInput(
            insight,
            self,
            stats,
            LiveDefenseAdvice.Calculate(stats, 0.64),
            new(new([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053),
            [3071],
            [8010, 9111, 9104, 8299, 8473, 8242, 5005, 5008, 5011],
            "布局样例 · 不代表当前对局"
        );
        roster["teamSummaries"] = new JArray("18 杀 · 41.2k · 领先 2.3k", "14 杀 · 38.9k");
        roster["liveRevision"] = "fixture-live-1";
        roster["liveSections"] = LivePanelSections.Build(input, id => $"布局装备 {id}");
        var waves = MinionWaveForecaster.Calculate(SampleWaves(), (long)roster["updatedAt"]!)!;
        // The sample is Summoner's Rift, which gets no advice; a fixed one shows the Howling Abyss layout.
        roster["waves"] = (waves with { DeathAdvice = SampleDeathAdvice() }).ToJson();
    }

    private static DeathTimingAdvice SampleDeathAdvice()
    {
        DeathTimingFactor[] factors =
        [
            new(1, "缺席约 29 秒，期间敌方兵线到不了我方外塔"),
            new(3, "对面 2 人阵亡（最长还剩 24 秒）"),
            new(2, "身上 2650 金，够买大件"),
            new(-1, "你是我方主要前排"),
        ];
        return new(DeathTimingVerdict.Good, [factors[1].Text, factors[2].Text], factors);
    }

    // Summoner's Rift at 14:06 so the page shows three lanes, the 14:00 interval change and a fallen inhibitor.
    public static LiveMatchSnapshot SampleWaves()
    {
        static LiveMatchEvent Structure(string name, double time, string structure) =>
            new(null, name, time, "", "") { Structure = structure };
        return new LiveMatchSnapshot(
            "CLASSIC",
            11,
            846,
            Enumerable
                .Range(0, 10)
                .Select(i => new LiveMatchPlayer(
                    $"p|{i}",
                    $"测试玩家 {i + 1}",
                    "Ahri",
                    $"样例英雄 {i + 1}",
                    i < 5 ? "ORDER" : "CHAOS",
                    i == 0,
                    9 + i % 5,
                    null
                ))
                .ToArray()
        )
        {
            Events =
            [
                Structure("MinionsSpawning", 30, ""),
                Structure("TurretKilled", 610, "Turret_T2_R_03_A"),
                Structure("TurretKilled", 680, "Turret_T2_R_02_A"),
                Structure("TurretKilled", 742, "Turret_T2_C_07_A"),
                Structure("InhibKilled", 780, "Barracks_T2_R1"),
                Structure("TurretKilled", 701, "Turret_T1_C_05_A"),
            ],
        };
    }

    private static string Encode(string path, int width = 48, int height = 48)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        using var source = Image.FromFile(path);
        using var small = new Bitmap(source, new Size(width, height));
        using var stream = new MemoryStream();
        small.Save(stream, width > 48 ? ImageFormat.Jpeg : ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }
}
