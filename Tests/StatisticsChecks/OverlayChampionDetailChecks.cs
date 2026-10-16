using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class OverlayChampionDetailChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        RequestIsolation();
        OpggRecommendations();
        MayhemRecommendations();
        CompleteMayhemAugments();
        Task.Run(CheckBridge).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: hero-detail request isolation, both teams, mode-specific recommendations, spell highlights, later equipment and response channel."
        );
    }

    private static void RequestIsolation()
    {
        string session = "instance:match",
            id = Guid.NewGuid().ToString("N");
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var request = new JObject
        {
            ["protocol"] = 1,
            ["id"] = id,
            ["session"] = session,
            ["updatedAt"] = now,
            ["slot"] = 5,
            ["championId"] = 103,
        };
        int[] slots = [1, 0, 0, 0, 0, 103, 0, 0, 0, 0];
        Check(
            OverlayDetailRequest.Parse(request, session, slots, now)?.ChampionId == 103,
            "Enemy hero can be selected without account identifiers"
        );
        Check(
            OverlayDetailRequest.Parse(request, "next-match", slots, now) == null,
            "Previous game requests are rejected"
        );
        Check(
            OverlayDetailRequest.Parse(request, session, slots, now + 30001) == null,
            "Stale requests cannot start new loads"
        );
        request["slot"] = 1;
        Check(
            OverlayDetailRequest.Parse(request, session, slots, now) == null,
            "Empty roster slots cannot request arbitrary heroes"
        );
        request["slot"] = -1;
        request["championId"] = 0;
        Check(
            OverlayDetailRequest.Parse(request, session, slots, now)?.Slot == -1,
            "Returning to roster cancels selection"
        );
        request["id"] = "../../other";
        Check(
            OverlayDetailRequest.Parse(request, session, slots, now) == null,
            "Requests only accept opaque identifiers"
        );
    }

    private static void OpggRecommendations()
    {
        foreach (string mode in new[] { "aram", "ranked", "urf", "nexus_blitz", "arena" })
        {
            var raw = JObject.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opgg", mode + ".json"))
            );
            var build = OpggBuild.Parse(raw, new("global", mode, "all", mode == "ranked" ? "mid" : "none", 103));
            var data = OverlayChampionDetailService.FromOpgg(build);
            var sections = (JArray)data["sections"]!;
            CheckSingleItemRates(build, sections);
            var coreOnly = OverlayChampionDetailService.FromOpgg(
                build with
                {
                    Data = new JObject { ["core_items"] = build.Data["core_items"]?.DeepClone() },
                }
            );
            Check(
                !coreOnly["sections"]![0]!["rows"]!.Any(),
                "Missing single-item statistics do not get fabricated from combinations"
            );
            Check(sections.All(s => (string?)s["tab"] != "augments"), "Other modes never show Mayhem recommendations");
            foreach (var row in sections.SelectMany(s => (JArray)s["rows"]!).Where(r => r["later"] != null))
            {
                var core = ((JArray)row["icons"]!).Select(i => (int)i["id"]!).ToHashSet();
                Check(
                    ((JArray)row["later"]!).All(i => !core.Contains((int)i["id"]!)),
                    "Later equipment excludes the chosen core combination"
                );
            }
            var spells = sections.Single(s => (string?)s["title"] == "召唤师技能");
            Check(spells["rows"]!.Count() <= 2, "Only most-used and highest-win spell highlights are shown");
            Check(!data.ToString().Contains("puuid"), "Details do not carry player identifiers");
        }
    }

    private static void CheckSingleItemRates(OpggBuild build, JArray sections)
    {
        var equipment = sections.Single(s => (string?)s["layout"] == "equipment");
        var singles = OpggItemSetService
            .Options(build.Data, "last_items")
            .Where(r => OpggItemSetService.Ids(r).Length == 1)
            .DistinctBy(r => OpggItemSetService.Ids(r)[0])
            .Take(30)
            .ToArray();
        Check(
            ReferenceEquals(sections.First, equipment),
            "The selected hero's individual recommendations appear before combinations"
        );
        Check(
            equipment["rows"]!
                .Select(r => (int)r["icons"]![0]!["id"]!)
                .SequenceEqual(singles.Select(r => OpggItemSetService.Ids(r)[0])),
            "OP.GG individual equipment comes from this hero's single-item statistics"
        );
        for (int i = 0; i < singles.Length; i++)
        {
            Check(
                (string?)equipment["rows"]![i]!["winRateText"]
                    == "胜率 "
                        + (
                            (
                                (double?)singles[i]["win_rate"]
                                ?? OpggBuild.Ratio(singles[i]["win"], singles[i]["play"])
                            )?.ToString("P1") ?? "—"
                        ),
                "Single-item win rate must not use a build-combination win rate"
            );
        }
    }

    private static void MayhemRecommendations()
    {
        var index = AramMetaIndex.Parse(
            JObject.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta", "index.json"))
            )
        );
        var detail = JObject.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta", "103.json"))
        );
        var mayhem = OverlayChampionDetailService.FromAramMeta(new(index, detail, true, DateTime.UtcNow), 103);
        Check(
            ((JArray)mayhem["sections"]!).Count(s => (string?)s["tab"] == "augments") == 3,
            "Mayhem includes all three rarities"
        );
        Check(
            ((JArray)mayhem["sections"]!).All(s => (string?)s["tab"] != "runes"),
            "Mayhem does not present a real rune recommendation"
        );
        Check(((string)mayhem["source"]!).StartsWith("arammeta"), "Full source name remains visible");
        var augmentRow = ((JArray)mayhem["sections"]!).First(s => (string?)s["tab"] == "augments")["rows"]![0]!;
        Check(
            (string?)augmentRow["search"] == ChineseInitials.Of((string?)augmentRow["icons"]![0]!["name"]),
            "Augment rows carry pinyin initials for the widget's search"
        );
        Check(ChineseInitials.Of("坦克引擎") == "tkyq", "Initials of common characters");
        Check(ChineseInitials.Of("Ult Bot 大招！") == "ultbotdz", "Letters stay, punctuation and spaces go");
        Check(ChineseInitials.Of(null) == "" && ChineseInitials.Of("〇") == "", "Empty and unknown characters");
        Check(
            ((JArray)mayhem["sections"]!).Any(s => (string?)s["tab"] == "builds" && s["rows"]!.Any()),
            "Mayhem includes equipment recommendations"
        );
        var mayhemEquipment = ((JArray)mayhem["sections"]!).First(s => (string?)s["tab"] == "builds");
        var mayhemSingles = AramMetaRecommendation
            .Read(new(index, detail, true, DateTime.UtcNow), 103, "singleItems")
            .Where(r => r.Ids.Length == 1)
            .DistinctBy(r => r.Ids[0])
            .Take(30)
            .ToArray();
        Check(
            (string?)mayhemEquipment["layout"] == "equipment"
                && mayhemEquipment["rows"]!
                    .Select(r => (int)r["icons"]![0]!["id"]!)
                    .SequenceEqual(mayhemSingles.Select(r => r.Ids[0])),
            "Mayhem individual recommendations retain the selected hero's source ordering"
        );
        for (int i = 0; i < mayhemSingles.Length; i++)
        {
            Check(
                (string?)mayhemEquipment["rows"]![i]!["pickRateText"]
                    == "使用率 " + (mayhemSingles[i].PickRate?.ToString("P1") ?? "—"),
                "Mayhem individual item usage is retained"
            );
        }

        Check(
            (
                (JArray)
                    OverlayChampionDetailService.FromAramMeta(new(index, null, true, DateTime.UtcNow), 103)["sections"]!
            ).All(s => (string?)s["tab"] == "augments"),
            "Missing detail does not discard cached augment recommendations"
        );
    }

    private static void CompleteMayhemAugments()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta");
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(fixturePath, "103-augments.json")));
        var index = AramMetaIndex.Parse(JObject.Parse(File.ReadAllText(Path.Combine(fixturePath, "index.json"))));
        index = index with { Augments = (JObject)fixture["augs"]! };
        var detail = (JObject)fixture["detail"]!;
        var page = new AramMetaPage(index, detail, true, DateTime.UtcNow);
        var categories = new[] { "kPrismatic", "kGold", "kSilver" };
        var pool = (JArray)detail["poolAugments"]!;
        var sections = ((JArray)OverlayChampionDetailService.FromAramMeta(page, 103)["sections"]!).ToArray();
        int count = 0;
        for (int i = 0; i < categories.Length; i++)
        {
            string category = categories[i];
            var expected = pool.Where(row =>
                    (string?)index.Augments[((int)row["id"]!).ToString()]?["rarity"] == category
                )
                .ToArray();
            var actual = AramMetaRecommendation.Read(page, 103, category);
            Check(
                actual.Length > 8
                    && actual.Select(row => row.Ids[0]).SequenceEqual(expected.Select(row => (int)row["id"]!)),
                "Complete hero pool is grouped by rarity without truncation or reordering"
            );
            Check(
                sections[i]["rows"]!
                    .Select(row => (int)row["icons"]![0]!["id"]!)
                    .SequenceEqual(actual.Select(row => row.Ids[0])),
                "Game Bar exposes every hero-specific statistical row, including rows after the eighth"
            );
            for (int n = 0; n < actual.Length; n++)
            {
                Check(
                    actual[n].WinRate == (double?)expected[n]["wr"]
                        && actual[n].PickRate == (double?)expected[n]["pick"],
                    "Full-pool rates are the hero's rates, not global augment rates"
                );
            }
            count += actual.Length;
        }
        Check(count == pool.Count && count == 129, "Captured hero's 129 augment statistics are all retained");
        var topOnly = page with { Detail = new JObject { ["top"] = detail["top"]!.DeepClone() } };
        foreach (string category in categories)
        {
            Check(
                AramMetaRecommendation.Read(topOnly, 103, category).Length == detail["top"]![category]!.Count(),
                "Legacy detail recommendations take precedence over the three-entry overview"
            );
            Check(
                AramMetaRecommendation.Read(page with { Detail = null }, 103, category).Length == 3,
                "Overview recommendations remain available before detail load or during offline fallback"
            );
        }
        pool.Add(pool[0].DeepClone());
        pool.Add(new JObject { ["id"] = 0, ["wr"] = 1 });
        pool.Add(new JObject { ["id"] = int.MaxValue, ["wr"] = 1 });
        pool.Add(JValue.CreateNull());
        Check(
            categories.Sum(category => AramMetaRecommendation.Read(page, 103, category).Length) == count,
            "Duplicate and unclassified pool entries cannot create additional cards"
        );
        Check(
            AramMetaRecommendation.Read(page, int.MaxValue, categories[0]).Length == 0,
            "A missing hero never inherits another hero's pool statistics"
        );
        pool.Clear();
        Check(
            AramMetaRecommendation.Read(page, 103, categories[0]).Length == 16,
            "An unavailable pool falls back to detailed recommendations"
        );
    }

    private static async Task CheckBridge()
    {
        string folder = Path.Combine(Path.GetTempPath(), "League-detail-" + Guid.NewGuid().ToString("N"));
        var bridge = new GameBarBridge(folder);
        try
        {
            bridge.Publish(
                new("1", 450, "ARAM", [new("private-key", "测试玩家", "private-account", 1, 103, 100, true)]),
                new Dictionary<string, PlayerChampionTier>(),
                new Dictionary<int, Image>(),
                _ => "九尾妖狐",
                _ => "测试缓存战绩",
                "one",
                "当前对局",
                true,
                true,
                true
            );
            string snapshot = Path.Combine(folder, "snapshot.json");
            var source = JObject.Parse(await BridgeCheckFiles.ReadAsync(snapshot, "Snapshot becomes available"));
            var request = new JObject
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString("N"),
                ["session"] = source["session"],
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["slot"] = 0,
                ["championId"] = 103,
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "detail-request.json"), request.ToString());
            var read = await bridge.ReadDetailRequestAsync();
            Check(read != null, "Current selection reaches main process");
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta");
            var fixture = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "103-augments.json")));
            var index = AramMetaIndex.Parse(
                JObject.Parse(await File.ReadAllTextAsync(Path.Combine(fixturePath, "index.json")))
            );
            index = index with { Augments = (JObject)fixture["augs"]! };
            var payload = OverlayChampionDetailService.FromAramMeta(
                new(index, (JObject)fixture["detail"]!, true, DateTime.UtcNow),
                103
            );
            payload["state"] = "ready";
            bridge.PublishDetail(read!, payload);
            bridge.Dispose();
            await bridge.Completion;
            var result = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "detail.json")));
            Check(
                (string?)result["requestId"] == read!.Id && (int?)result["championId"] == 103,
                "Reply identifies the requested hero, not a later slot occupant"
            );
            Check(
                ((JArray)result["sections"]!).Sum(section => section["rows"]!.Count()) == 129,
                "All hero augment statistics survive the main-process to Game Bar reply channel"
            );
            Check(
                new FileInfo(Path.Combine(folder, "detail.json")).Length < 4000000,
                "Full statistical payload fits within the existing widget reply size limit"
            );
            Check(!result.ToString().Contains("private-"), "Private roster keys are absent from detail responses");
            Check(
                !Directory.EnumerateFiles(folder, "*.tmp").Any(),
                "Response writes are atomic and clean up temporary files"
            );
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
