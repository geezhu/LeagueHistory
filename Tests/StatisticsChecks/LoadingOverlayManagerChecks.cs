using System.Net;
using System.Reflection;
using System.Text;
using League.Clients;
using League.Managers;
using League.Models;
using League.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The loading overlay manager's offline surface: disposal, backend switching, the desktop preview shell, the roster
/// and live helpers driven directly, one simulated poll against stub clients, and the Game Bar files it reads and
/// writes. Nothing here plays audio, types into the game, opens a folder or downloads statistics: the widget files
/// go to a temporary folder, and the LCU, Live Client and arammeta clients are replaced with stubs.
/// </summary>
internal static class LoadingOverlayManagerChecks
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string GameBarBackend = "GameBar";
    private const string DesktopBackend = "Desktop";
    private const string SelfPuuid = "self-puuid";

    public static void Run() => WinFormsCheckHost.Run(RunCore);

    private static void RunCore()
    {
        Lifecycle();
        Backends();
        Ticks();
        Preview();
        ClearMatch();
        LiveHelpers();
        Poll();
        PollEdges();
        PollSwitches();
        PollCancellation();
        AramHelpers();
        LiveEdges();
        ResourceEdges();
        WidgetFiles();
        ItemSounds();
        LocalDataEdges();
        Console.WriteLine(
            "PASS: overlay disposal, backend switching, the desktop preview shell, roster/live helpers, polls against stub clients, aram mode helpers, the resource edges, the Game Bar snapshot and the widget's own files."
        );
    }

    #region 生命周期与后端
    private static void Lifecycle()
    {
        var overlay = new Overlay(GameBarBackend);
        var icons = overlay.Field<Dictionary<int, Image>>("_icons");
        icons[99] = new Bitmap(4, 4);
        overlay.Dispose();
        overlay.Dispose();
        Check(icons.Count == 0, "Dispose releases the champion icons the manager owns");
        Check(overlay.Field<bool>("_disposed"), "Dispose marks the manager disposed");
        overlay.Call("RefreshDisplaySettings");
        overlay.Call("PaintSnapshot");
        overlay.Call("SchedulePaint");
        overlay.Call("PublishGameBar");
        Check(overlay.Field<object?>("_window") == null, "Nothing creates a window after disposal");
        overlay.CallAsync("UpdateRespawnSoundsAsync", Roster(), Snapshot(false), overlay.Generation);
        overlay.Call("ClearMatch");
        Console.WriteLine("Disposed overlay: dimissal paths, icons and post-dispose calls.");
    }

    private static void Backends()
    {
        using var overlay = new Overlay(DesktopBackend);
        Check(overlay.Status().Length > 0, "The settings page can show the Game Bar state");
        overlay.Config.LoadingOverlayBackend = GameBarBackend;
        overlay.Call("ConfigureBackend");
        Check(overlay.Field<bool>("_configuredGameBar"), "Selecting the Game Bar backend configures it");
        overlay.Call("ConfigureBackend");
        Check(overlay.Field<bool>("_configuredGameBar"), "Configuring the same backend twice changes nothing");
        overlay.Config.EnableLoadingOverlay = false;
        overlay.Call("ConfigureBackend");
        Check(
            overlay.Field<bool?>("_configuredEnabled") == false && !overlay.Display.Requested,
            "Turning the feature off is observed at once and clears the display request"
        );
        overlay.Config.EnableLoadingOverlay = true;
        overlay.Config.LoadingOverlayBackend = DesktopBackend;
        overlay.Call("ConfigureBackend");
        Check(
            !overlay.Field<bool>("_configuredGameBar") && overlay.Field<bool?>("_configuredEnabled") == true,
            "Switching back to the desktop backend is observed"
        );
        overlay.SetPhase("InProgress");
        overlay.SetPhase("GameStart");
        overlay.Config.LoadingOverlayBackend = GameBarBackend;
        overlay.Call("ConfigureBackend");
        overlay.Config.EnableLoadingOverlay = false;
        overlay.Call("ConfigureBackend");
        overlay.Config.EnableLoadingOverlay = true;
        overlay.Config.LoadingOverlayBackend = DesktopBackend;
        overlay.Call("ConfigureBackend");
        Check(
            !overlay.Field<bool>("_configuredGameBar")
                && overlay.Field<bool?>("_configuredEnabled") == true
                && !overlay.Display.Requested,
            "Leaving the Game Bar for the desktop stops its publishing and changes no display request"
        );
        Console.WriteLine("Backend switching, enable flag and status text.");
    }

    private static void Ticks()
    {
        using var lcu = new LcuStub(LcuRoute);
        using var overlay = new Overlay(GameBarBackend);
        overlay.SetPhase("");
        overlay.Tick("_detailPoll");
        PumpUntil(() => !overlay.Field<bool>("_readingDetail"));
        overlay.Tick("_poll");
        PumpUntil(() => overlay.Field<string>("_phase") == "InProgress");
        overlay.StopTimers();
        Check(overlay.Field<bool>("_busy") == false, "The poll timer leaves no read in flight");
        Console.WriteLine("Timer ticks: backend configuration, the poll and the detail/chat/voice readers.");
    }

    private static void Preview()
    {
        using var overlay = new Overlay(DesktopBackend);
        overlay.SetPhase("Lobby");
        overlay.Call("RefreshDisplaySettings");
        Check(overlay.Window == null, "A lobby refresh alone does not build the shell");
        overlay.Call("ShowPreview");
        Check(overlay.Window != null, "The settings preview creates the overlay shell");
        var window = overlay.Window!;
        Check(window.Visible && overlay.Display.Preview, "The settings preview shows the shell");
        Check(
            Children(window).OfType<Label>().Any(label => label.Text.Contains("显示测试")),
            "The preview shell says it is a test"
        );
        Check(window.Bounds.Width > 0 && window.Bounds.Height > 0, "The preview is placed on a working area");
        overlay.Call("Dismiss");
        Check(!window.Visible && overlay.Display.Suppressed, "Hiding the preview from its own button is remembered");
        AutomaticDisplay(overlay, window);
        overlay.Call("PaintSnapshot");
        Check(ReferenceEquals(overlay.Window, window), "Painting keeps the same shell");
        Console.WriteLine("Desktop shell, preview title, dismiss and automatic display.");
    }

    private static void AutomaticDisplay(Overlay overlay, Form window)
    {
        overlay.Display.Reset();
        overlay.SetPhase("GameStart");
        overlay.Call("RefreshDisplaySettings");
        overlay.StopTimers();
        Check(overlay.Display.Requested, "The loading phase requests the shell without any roster");
        Check(
            !window.Visible && overlay.Field<string>("_visibilityReason")!.Contains("游戏窗口"),
            "Without a running game the shell stays hidden and says why: " + overlay.Field<string>("_visibilityReason")
        );
        overlay.Call("Follow");
        Check(
            overlay.Field<string>("_visibilityReason")!.Contains("游戏窗口"),
            "Waiting for the game window is not repeated within the search interval"
        );
        overlay.SetPhase("Lobby");
        overlay.Call("Follow");
        Check(
            overlay.Field<string>("_visibilityReason")!.Contains("阶段"),
            "Following stops when the phase is no longer a loading or game phase"
        );
        overlay.Call("ShowPreview");
        overlay.Call("RefreshDisplaySettings");
        overlay.Call("Follow");
        Check(overlay.Display.Preview, "A preview request survives a refresh and is not followed by the game");
        overlay.Call("ShowPreview");
        Check(ReferenceEquals(overlay.Window, window), "A second preview reuses the shell");
        overlay.Config.EnableLoadingOverlay = false;
        overlay.Call("ShowPreview");
        overlay.Call("RefreshDisplaySettings");
        Check(
            !overlay.Display.Preview && !overlay.Display.Requested && !window.Visible,
            "A preview and a refresh do nothing while the feature is switched off, and the shell hides"
        );
        overlay.Config.EnableLoadingOverlay = true;
        overlay.Call("Dismiss");
        Check(!overlay.Display.Requested, "Dismissing a withdrawn preview keeps the request cleared");
    }

    private static void ClearMatch()
    {
        using var overlay = new Overlay(DesktopBackend);
        overlay.SetPhase("InProgress");
        overlay.Call("ApplyRoster", Roster(), Snapshot(false));
        overlay.Call("ClearMatch");
        Check(
            overlay.Field<LoadingRoster?>("_roster") == null && overlay.Field<string>("_match") == "",
            "Leaving a match drops the roster and the match key"
        );
        Check(
            overlay.Field<string>("_status") == LoadingRoster.WaitingStatus("InProgress"),
            "The waiting status follows the gameflow phase"
        );
        overlay.Call("ShowPreview");
        overlay.Call("ClearMatch");
        Check(overlay.Display.Preview, "Clearing a match keeps an explicit preview open");
        var player = Roster().Players[0];
        Check(
            ((string)overlay.Call("Details", player)!).Contains("暂无"),
            "A player without cached history says so instead of waiting"
        );
    }

    #endregion

    #region 名单与实时数据
    private static void LiveHelpers()
    {
        using var overlay = new Overlay(DesktopBackend);
        overlay.SetPhase("InProgress");
        var roster = Roster();
        overlay.Call("ApplyRoster", roster, Snapshot(false));
        Check(overlay.Field<LoadingRoster?>("_roster")!.Players.Length == 3, "The roster is kept for the cards");
        Check(
            overlay.Field<string>("_match")!.StartsWith("555", StringComparison.Ordinal),
            "The match key names the game"
        );
        Check(overlay.LiveState != null, "A live snapshot replaces the loading statistics");
        var generation = overlay.Generation;
        PumpUntil(() =>
            overlay.Field<object?>("_summonerSpells") != null && overlay.Field<object?>("_baseStats") != null
        );
        overlay.Call("ApplyRoster", roster, Snapshot(false));
        CheckLiveState(overlay, roster);
        overlay.Call("ApplyRoster", roster, Snapshot(true));
        Check(
            overlay.Field<int?>("_augmentsOpenedAt") == 11 && overlay.Field<bool>("_selfDead"),
            "A dead or just-started self records when the augment shop was open"
        );
        CheckRespawnFormula(overlay, generation);
        overlay.Call("ApplyRoster", roster, null);
        Check(overlay.LiveState != null, "A missed poll keeps the last scoreboard");
        Console.WriteLine("Roster application, live state, health, haste, summoners, waves and respawn checks.");
    }

    private static void CheckLiveState(Overlay overlay, LoadingRoster roster)
    {
        var state = overlay.LiveState!;
        Check(state.Health.Count == roster.Players.Length, "Every matched player has a health line");
        Check(state.Haste.Count == roster.Players.Length, "Every matched player has a haste line");
        Check(
            state.Summoners.Count == roster.Players.Length && state.Summoners.Values.All(spells => spells.Length == 2),
            "Both summoner spells of every matched player are described: "
                + string.Join(",", state.Summoners.Select(entry => entry.Key + "=" + entry.Value.Length))
        );
        Check(state.Effects.Count == roster.Players.Length, "Augment effects are summed per player");
        Check(state.Waves != null && state.Waves.Spawns.Length > 0, "The wave forecast carries the next waves");
    }

    private static void CheckRespawnFormula(Overlay overlay, int generation)
    {
        overlay.Call("CheckRespawnFormula", Snapshot(true));
        Check(
            overlay.Field<HashSet<string>>("_deadPlayers").Count == 1,
            "Each new death is checked against the respawn formula once"
        );
        overlay.Call("CheckRespawnFormula", Snapshot(false));
        Check(overlay.Field<HashSet<string>>("_deadPlayers").Count == 0, "A revived player leaves the check");
        overlay.Write(
            "_augmentSelections",
            new[] { new AugmentSelection("", TrackedAugment.InfiniteRecursion, new(30, 2)) }
        );
        var stacks = (Array)overlay.Call("CountAugmentStacks", Roster(), Snapshot(true))!;
        Check(stacks.Length == 1, "A recorded augment without a player is credited to the widget's own player");
        var effects = (System.Collections.IDictionary)overlay.Call("SumAugmentEffects", Roster(), stacks)!;
        Check(effects.Count == 3, "Augment effects exist for every champion on the roster");
        overlay.Call("TrackAugments", Snapshot(true));
        Check(overlay.Field<int?>("_augmentsOpenedAt") == 11, "The augment shop time follows the player's level");
        overlay.CallAsync("ReadAugmentSelectionsAsync", generation);
        overlay.CallAsync("ReadSummonerTimersAsync", generation);
        overlay.CallAsync("ReadControlSettingsAsync", generation);
        overlay.Call("UpdateSurvivalSounds", Roster(), Insight());
    }

    private static LiveMatchInsight Insight() => LiveMatchInsight.Calculate(Roster(), Snapshot(false), _ => 0)!;

    private static LoadingRoster AramRoster() =>
        new(
            "777",
            2400,
            "ARAM",
            [
                new("self", "自己#0001", SelfPuuid, 11, 99, 100, true),
                new("ally", "队友#0002", "ally-puuid", 12, 25, 100, false),
                new("enemy", "对手#0003", "enemy-puuid", 13, 1, 200, false),
            ]
        );

    private static LiveMatchSnapshot AramSnapshot(bool dead) =>
        LiveMatchSnapshot.Parse(JObject.Parse(LiveBody(dead, "ARAM")));

    /// <summary>A named mode reaches the wave forecast, the damage mix, the composition page and the augment kinds.</summary>
    private static void AramHelpers()
    {
        using var lcu = new LcuStub(LcuRoute);
        using var overlay = new Overlay(GameBarBackend);
        overlay.SetPhase("InProgress");
        var roster = AramRoster();
        overlay.Call("ApplyRoster", roster, AramSnapshot(false));
        Check(overlay.Field<string>("_liveMode") == "ARAM", "A live game names its mode for the cooldown page");
        Check(overlay.LiveState!.Waves != null, "The wave forecast is built for a named mode");
        overlay.Call("PublishGameBar");
        PumpUntil(() => overlay.Field<object?>("_composition") != null);
        Check(overlay.Field<bool>("_enemyShareRead"), "The enemies' damage mix is read once from the statistics index");
        overlay.Call("ApplyRoster", roster, AramSnapshot(true));
        Check(overlay.Field<bool>("_selfDead"), "A death after a living snapshot is followed");
        AugmentKinds(overlay, roster);
    }

    private static void AugmentKinds(Overlay overlay, LoadingRoster roster)
    {
        overlay.Write(
            "_augmentSelections",
            new[]
            {
                new AugmentSelection("", TrackedAugment.InfiniteRecursion, new(10, 1)),
                new AugmentSelection("", TrackedAugment.SoulEater, new(10, 2)) { Choice = "first" },
                new AugmentSelection("", TrackedAugment.Goliath, new(10, 0)),
            }
        );
        overlay.Write("_ultimateHaste", new Dictionary<string, double> { ["self"] = 30 });
        var stacks = (LiveAugmentStack[])overlay.Call("CountAugmentStacks", roster, AramSnapshot(true))!;
        Check(
            stacks.Length == 3
                && stacks[0].State.Note.Length > 0
                && stacks[1].State.Note.Contains("手动")
                && stacks[2].State.Note.Contains("固定"),
            "Takedown, manual and fixed augments are counted and explained differently"
        );
        var effects = (Dictionary<string, AugmentEffects>)overlay.Call("SumAugmentEffects", roster, stacks)!;
        Check(
            effects["self"].Cooldown.UltimateHaste == 30,
            "The hand-entered ultimate haste reaches the cooldown rules"
        );
    }

    /// <summary>The live helpers' own guards: an unknown side, a retired match, a busy panel and a broken source.</summary>
    private static void LiveEdges()
    {
        using var overlay = new Overlay(GameBarBackend);
        overlay.SetPhase("InProgress");
        overlay.Call("ApplyRoster", Roster(), Snapshot(false));
        var unidentified = Roster() with
        {
            Players = Roster().Players.Select(player => player with { Self = false }).ToArray(),
        };
        overlay.Call("ApplyRoster", unidentified, UnknownSnapshot());
        Check(overlay.LiveState == null, "A snapshot whose sides cannot be told apart shows no scoreboard");
        int generation = overlay.Generation;
        overlay.CallAsync("UpdateRespawnSoundsAsync", Roster(), Snapshot(false), generation + 1);
        Check(overlay.Field<string>("_phase") == "InProgress", "A read started for an earlier match publishes nothing");
        var waves = LiveMatchForecast();
        overlay.Call("ReportUnknownStructures", waves);
        overlay.Call("ReportUnknownStructures", waves);
        Check(
            overlay.Field<HashSet<string>>("_unknownStructures").Count == 1,
            "An unrecognized structure is logged once, not on every poll"
        );
        overlay.Write("_liveBusy", true);
        overlay.Call("RefreshLivePanelAsync", Roster(), Snapshot(false), Insight(), generation);
        Check(overlay.Field<bool>("_liveBusy"), "A panel refresh already in flight is not started again");
        overlay.Write("_liveBusy", false);
        BrokenPanel(overlay, Roster(), Snapshot(false), Insight(), generation);
    }

    /// <summary>A detail service that is no longer there must not take the live panel down with it.</summary>
    private static void BrokenPanel(
        Overlay overlay,
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight,
        int generation
    )
    {
        var details = overlay.Field<object>("_championDetails");
        overlay.Write("_championDetails", null);
        try
        {
            overlay.CallAsync("RefreshLivePanelAsync", roster, snapshot, insight, generation);
        }
        finally
        {
            overlay.Write("_championDetails", details);
        }

        Check(overlay.Field<bool>("_liveBusy") == false, "A failed panel refresh releases the panel for the next poll");
    }

    /// <summary>Statistics and resource files that are empty or broken are reported, never downloaded.</summary>
    private static void ResourceEdges()
    {
        using var overlay = new Overlay(GameBarBackend);
        overlay.SetPhase("InProgress");
        var roster = Roster();
        overlay.Call("ApplyRoster", roster, Snapshot(false));
        PumpUntil(() => overlay.Field<object?>("_baseStats") != null);
        var insight = Insight();
        var widened = roster with
        {
            Players = [.. roster.Players, new("extra", "不在场内#0009", "extra-puuid", 19, 99, 100, false)],
        };
        Check(
            (
                (System.Collections.IDictionary)
                    overlay.Call("DescribeHealth", widened, Snapshot(false), insight, Effects())!
            ).Count == roster.Players.Length,
            "A player the snapshot cannot match is left out of the health and haste lines"
        );
        Check(
            (
                (System.Collections.IDictionary)overlay.Call("DescribeSummoners", widened, Snapshot(false), insight)!
            ).Count == roster.Players.Length,
            "The same player is left out of the summoner spells"
        );
        BaseStatsEdges(overlay);
        SummonerDataEdges(overlay);
        MasteryEdges(overlay, roster);
        ItemStatsEdges(overlay);
    }

    private static Dictionary<string, AugmentEffects> Effects() => new();

    private static void BaseStatsEdges(Overlay overlay)
    {
        overlay.Write("_baseStats", null);
        overlay.Write("_baseStatsData", new ChampionBaseStatsData(Path.Combine(overlay.Folder, "empty"), "16.20"));
        overlay.CallAsync("LoadBaseStatsAsync");
        Check(
            overlay.Field<object?>("_baseStats") == null && overlay.Field<long>("_baseStatsRetry") > 0,
            "An empty champion statistics file keeps health estimates hidden and is retried later"
        );
        overlay.Write("_baseStatsRetry", 0L);
        overlay.Write("_baseStatsData", new ChampionBaseStatsData(Path.Combine(overlay.Folder, "bad"), "16.20"));
        overlay.CallAsync("LoadBaseStatsAsync");
        Check(
            overlay.Field<object?>("_baseStats") == null && overlay.Field<long>("_baseStatsRetry") > 0,
            "A broken champion statistics file is reported and retried instead of failing the overlay"
        );
    }

    private static void SummonerDataEdges(Overlay overlay)
    {
        overlay.Write("_summonerSpells", null);
        overlay.Write("_summonerRetry", 0L);
        overlay.Write("_summonerData", new SummonerSpellData(Path.Combine(overlay.Folder, "empty"), "16.20"));
        overlay.CallAsync("LoadSummonerSpellsAsync");
        Check(
            overlay.Field<object?>("_summonerSpells") == null && overlay.Field<long>("_summonerRetry") > 0,
            "A spell list without spells is retried in a minute"
        );
        overlay.Write("_summonerRetry", 0L);
        overlay.Write("_summonerData", new SummonerSpellData(Path.Combine(overlay.Folder, "bad"), "16.20"));
        overlay.CallAsync("LoadSummonerSpellsAsync");
        Check(overlay.Field<object?>("_summonerSpells") == null, "A broken spell list only loses the spell names");
        overlay.Write("_summonerModes", null);
        overlay.Write("_summonerModesRetry", 0L);
        overlay.Write("_summonerData", new SummonerSpellData(Path.Combine(overlay.Folder, "empty"), "16.20"));
        overlay.CallAsync("LoadSummonerModesAsync");
        Check(
            overlay.Field<object?>("_summonerModes") != null,
            "A mode table without overrides leaves the spells' own cooldowns"
        );
        overlay.Write("_summonerModes", null);
        overlay.Write("_summonerModesRetry", 0L);
        overlay.Write("_summonerData", new SummonerSpellData(Path.Combine(overlay.Folder, "bad"), "16.20"));
        overlay.CallAsync("LoadSummonerModesAsync");
        Check(
            overlay.Field<long>("_summonerModesRetry") > 0,
            "A broken mode table keeps the spells' own cooldowns and is retried much later"
        );
    }

    private static void MasteryEdges(Overlay overlay, LoadingRoster roster)
    {
        var mastery = roster.Players[0];
        overlay
            .Field<HashSet<string>>("_masteryRequests")
            .Add($"{overlay.Generation}:{mastery.Key}:{mastery.ChampionId}");
        overlay.CallAsync("LoadMastery", mastery, overlay.Generation);
        Check(
            !overlay.Field<Dictionary<string, PlayerChampionMastery>>("_masteries").ContainsKey(mastery.Key),
            "The same mastery is not requested twice for one match"
        );
        var hidden = roster.Players[1] with { IdentityHidden = true };
        overlay.CallAsync("LoadMastery", hidden, overlay.Generation);
        Check(
            overlay.Field<Dictionary<string, PlayerChampionMastery>>("_masteries")[hidden.Key].ChampionId == 25,
            "A player hiding their identity gets the unavailable mastery, not a request"
        );
        var unnamed = roster.Players[0] with { Puuid = "" };
        overlay.Field<HashSet<string>>("_masteryRequests").Clear();
        overlay.CallAsync("LoadMastery", unnamed, overlay.Generation);
        Check(
            overlay.Field<Dictionary<string, PlayerChampionMastery>>("_masteries").ContainsKey(unnamed.Key),
            "The player's own mastery falls back to the identity the client reported"
        );
    }

    private static void ItemStatsEdges(Overlay overlay)
    {
        var items = League.FormMain.Globals.resLoading.Items.Items;
        items.Add(
            new Item
            {
                Id = 3157,
                Name = "中娅沙漏",
                Description = "法术强度 +70",
            }
        );
        try
        {
            overlay.Call("ItemStatsFor", 3157);
            var cached = overlay.Call("ItemStatsFor", 3157);
            Check(cached != null, "An item's statistics are read once and kept");
            overlay.Call("ItemStatsFor", 223157);
            Check(
                overlay.Call("ItemStatsFor", 223157) != null,
                "A mode copy of an item carries the original's statistics"
            );
        }
        finally
        {
            items.RemoveAll(item => item.Id == 3157);
        }
    }

    /// <summary>A forecast with a structure name the parser has never seen.</summary>
    private static MinionWaveForecast LiveMatchForecast() =>
        new("Howling Abyss", 200, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), [], ["first"], "0 金币", [])
        {
            UnknownStructures = ["Turret_Unseen_Structure"],
        };

    /// <summary>The survival sounds load from the folder; nothing is played and nothing is downloaded.</summary>
    private static void ItemSounds()
    {
        using var overlay = new Overlay(GameBarBackend);
        foreach (string file in ItemSoundLibrary.Files.Values.Distinct(StringComparer.Ordinal))
        {
            File.WriteAllBytes(Path.Combine(overlay.Folder, Path.ChangeExtension(file, ".wav")), [0, 1, 2, 3]);
        }

        overlay.Write(
            "_controlSettings",
            new ControlSettings(true, false) { SummonerSound = false, CannonSound = CannonSoundModes.Off }
        );
        overlay.CallAsync("FetchItemSoundsAsync");
        Check(overlay.Field<bool>("_itemSoundsFetched"), "The survival sounds are fetched once a team sounds");
        Check(
            overlay.CallAsyncResult<byte[]?>("LoadItemSoundAsync", 9999) == null,
            "An item the library has no sound for stays silent"
        );
        Check(
            overlay.CallAsyncResult<byte[]?>("LoadItemSoundAsync", 3157) is { Length: 4 },
            "A cached sound is read from the folder, never played"
        );
        overlay.CallAsync("PlayItemSoundAsync", 9999);
        Check(overlay.Field<object?>("_survivalPlayer") == null, "A purchase without a sound plays nothing");
        BrokenSoundFile(overlay);
    }

    /// <summary>A sound file another program is holding open is reported, not downloaded again.</summary>
    private static void BrokenSoundFile(Overlay overlay)
    {
        string path = Path.Combine(overlay.Folder, "Zhonya's_Hourglass_active_SFX.wav");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Check(
            overlay.CallAsyncResult<byte[]?>("LoadItemSoundAsync", 3157) == null,
            "An unreadable sound file only silences that alert"
        );
    }

    /// <summary>The two lookups that do reach local data: the main window's cache and a champion icon on disk.</summary>
    private static void LocalDataEdges()
    {
        using var overlay = new Overlay(DesktopBackend);
        var cards = overlay.Owner._playerCardManager!;
        var cache = (PlayerCardCache)ReadField(cards, "_cache")!;
        cache.AddOrUpdateCache(
            11,
            new PlayerMatchInfo
            {
                Player = new PlayerInfo
                {
                    Puuid = SelfPuuid,
                    SummonerId = 11,
                    SoloRank = "黄金 III",
                    FlexRank = "白银 I",
                },
                WinHistory = [true, false, true],
            }
        );
        var details = (string)overlay.Call("Details", Roster().Players[0])!;
        Check(
            details.Contains("黄金 III") && details.Contains("3 场"),
            "A cached history is described without asking the client again: " + details
        );
        ChampionIcon(overlay);
    }

    /// <summary>A champion icon already in the asset cache is read instead of requested.</summary>
    private static void ChampionIcon(Overlay overlay)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "champion");
        string file = Path.Combine(folder, "Lux.png");
        Directory.CreateDirectory(folder);
        WriteIcon(file);
        var champions = League.FormMain.Globals.resLoading.Champions.Champions;
        try
        {
            champions.Add(new Champion { Id = 99, Name = "Lux" });
            overlay.Write("_icons", new Dictionary<int, Image>());
            overlay.CallAsync("LoadIcon", 99);
            Check(
                overlay.Field<Dictionary<int, Image>>("_icons").ContainsKey(99),
                "A cached champion icon is put on the cards"
            );
        }
        finally
        {
            champions.RemoveAll(champion => champion.Id == 99);
            File.Delete(file);
        }
    }

    #endregion

    #region 轮询

    private static void Poll()
    {
        using var lcu = new LcuStub(LcuRoute);
        using var overlay = new Overlay(GameBarBackend);
        overlay.SetLive(LiveBody(false));
        overlay.CallAsync("PollAsync");
        overlay.StopTimers();
        Check(overlay.Field<string>("_phase") == "InProgress", "The gameflow phase is read from the client");
        Check(
            Environment.TickCount64 - overlay.Field<long>("_lastPhaseRead") < 15000,
            "The phase read is dated, so the display can trust it"
        );
        var roster = overlay.Field<LoadingRoster?>("_roster")!;
        Check(
            roster.Players.Length == 3 && roster.Players.Any(p => p.ChampionId == 99 && p.Self),
            "The client and live rosters merge by confirmed identity"
        );
        Check(overlay.Field<string>("_status")!.Contains("当前对局"), "The status names the current match");
        Check(overlay.LiveState != null, "The poll builds the live state");
        PumpUntil(() => overlay.Field<Dictionary<string, PlayerChampionTier>>("_ranks")!.Count == 3);
        PumpUntil(() => overlay.Field<Dictionary<string, PlayerChampionMastery>>("_masteries")!.Count == 3);
        Check(!overlay.Field<HashSet<int>>("_iconRequests")!.Any(), "Icon requests do not stay in flight");
        PollFailure(overlay);
        Console.WriteLine("Gameflow phase, roster merge, statistics requests and poll failure handling.");
    }

    private static void PollFailure(Overlay overlay)
    {
        var failing = new LcuStub(_ => Missing());
        try
        {
            overlay.CallAsync("PollAsync");
            Check(
                overlay.Field<string>("_status")!.Contains("中断"),
                "A failing client read is reported without hiding the cached match: " + overlay.Field<string>("_status")
            );
        }
        finally
        {
            failing.Dispose();
        }
    }

    /// <summary>The poll's edge paths: a missing client, a lobby phase, a repaired flow, a dead live client.</summary>
    private static void PollEdges()
    {
        using (var noClient = new LcuStub(null))
        using (var overlay = new Overlay(GameBarBackend))
        {
            overlay.CallAsync("PollAsync");
            Check(
                overlay.Field<string>("_phase") == "" && overlay.Field<LoadingRoster?>("_roster") == null,
                "Without a client the poll clears the match instead of showing stale data"
            );
        }

        using (var lcu = new LcuStub(LobbyRoute))
        using (var overlay = new Overlay(GameBarBackend))
        {
            overlay.CallAsync("PollAsync");
            Check(
                overlay.Field<string>("_phase") == "Lobby" && overlay.Field<LoadingRoster?>("_roster") == null,
                "A phase outside a game keeps the phase and clears the roster"
            );
        }

        using (var lcu = new LcuStub(ConflictRoute))
        using (var overlay = new Overlay(GameBarBackend))
        {
            overlay.SetLive(LiveBody(false));
            overlay.CallAsync("PollAsync");
            var merged = overlay.Field<LoadingRoster?>("_roster")!;
            Check(
                merged.Players.Length == 3 && merged.Players.Any(player => player.Self && player.ChampionId == 99),
                "A session holding an account twice is replaced by the game's own list: "
                    + string.Join(",", merged.Players.Select(player => player.Key + "/" + player.ChampionId))
            );
        }

        using (var lcu = new LcuStub(BareRoute))
        using (var overlay = new Overlay(GameBarBackend))
        {
            overlay.SetLive(LiveBody(false));
            overlay.CallAsync("PollAsync");
            var bare = overlay.Field<LoadingRoster?>("_roster");
            Check(
                bare != null && bare.Players.Length == 3,
                "A session without game data still yields the live roster: "
                    + (
                        bare == null
                            ? "null"
                            : bare.Players.Length + " players, phase " + overlay.Field<string>("_phase")
                    )
            );
        }

        using (var lcu = new LcuStub(LcuRoute))
        using (var overlay = new Overlay(GameBarBackend))
        {
            overlay.FailLive();
            overlay.CallAsync("PollAsync");
            Check(
                overlay.Field<LoadingRoster?>("_roster")!.Players.Length == 3 && overlay.LiveState == null,
                "A live client that is not answering leaves the client roster in place"
            );
            overlay.Write("_lastPhaseRead", 0L);
            overlay.Call("HandlePollFailure");
            Check(
                overlay.Field<string>("_phase") == "" && overlay.Field<string>("_status").Contains("等待"),
                "A stale phase read is cleared instead of trusted"
            );
        }

        RankAndIconEdges();
    }

    private static void RankAndIconEdges()
    {
        using var lcu = new LcuStub(LcuRoute);
        using var overlay = new Overlay(GameBarBackend);
        overlay.SetPhase("InProgress");
        overlay.Call("ApplyRoster", Roster(), Snapshot(false));
        var players = overlay.Field<LoadingRoster>("_roster")!.Players;
        int generation = overlay.Generation;
        overlay.CallAsync("LoadRank", players[0], generation, Flow());
        overlay.CallAsync("LoadRank", players[0], generation, Flow());
        Check(
            overlay.Field<Dictionary<string, PlayerChampionTier>>("_ranks")[players[0].Key].Rate.Length > 0,
            "Every champion's statistics are requested once per match"
        );
        overlay.CallAsync("LoadRank", players[2], generation + 1, Flow());
        Check(
            !overlay.Field<Dictionary<string, PlayerChampionTier>>("_ranks").ContainsKey(players[2].Key),
            "A statistic read from an earlier match is dropped"
        );
        overlay.CallAsync("LoadRank", players[1], generation, null);
        Check(
            overlay.Field<Dictionary<string, PlayerChampionTier>>("_ranks")[players[1].Key].Rate == "统计暂不可用",
            "A failed statistic read shows a placeholder and is not retried at once"
        );
        overlay.CallAsync("LoadRank", players[1], generation, Flow());
        overlay.CallAsync("LoadRank", players[2] with { ChampionId = 7 }, generation, null);
        Check(
            !overlay.Field<Dictionary<string, PlayerChampionTier>>("_ranks").ContainsKey(players[2].Key),
            "A statistic read that failed for a champion no longer on the roster is dropped"
        );
        overlay.Write("_icons", new Dictionary<int, Image> { [99] = new Bitmap(4, 4) });
        overlay.CallAsync("LoadIcon", 99);
        Check(overlay.Field<Dictionary<int, Image>>("_icons").Count == 1, "A cached icon is not requested again");
    }

    /// <summary>A poll that must not run: the feature is off, or an earlier poll is still reading.</summary>
    private static void PollSwitches()
    {
        using var lcu = new LcuStub(LcuRoute);
        using var overlay = new Overlay(GameBarBackend);
        overlay.Config.EnableLoadingOverlay = false;
        overlay.CallAsync("PollAsync");
        Check(overlay.Field<string>("_phase") == "", "A switched-off overlay does not read the gameflow phase");
        overlay.Config.EnableLoadingOverlay = true;
        overlay.Write("_busy", true);
        overlay.CallAsync("PollAsync");
        Check(overlay.Field<string>("_phase") == "", "A poll already in flight is not started a second time");
        overlay.Write("_busy", false);
        overlay.SetLive(LiveBody(false));
        overlay.SetLiveHook(() => overlay.Config.EnableLoadingOverlay = false);
        overlay.CallAsync("PollAsync");
        Check(
            overlay.Field<LoadingRoster?>("_roster") == null,
            "A feature switched off while the game is being read publishes nothing"
        );
        overlay.Config.EnableLoadingOverlay = true;
    }

    /// <summary>Shutdown and disposal while a poll is waiting for the client.</summary>
    private static void PollCancellation()
    {
        using (var lcu = new LcuStub(SlowRoute, slow: true))
        using (var overlay = new Overlay(GameBarBackend))
        {
            var poll = overlay.CallStart("PollAsync");
            Thread.Sleep(60);
            overlay.CancelLife();
            PumpUntil(() => poll.IsCompleted);
            Check(
                overlay.Field<bool>("_busy") == false && !overlay.Field<string>("_status").Contains("中断"),
                "Shutting the overlay down cancels a poll without reporting a connection failure: "
                    + overlay.Field<string>("_status")
                    + " roster="
                    + (overlay.Field<LoadingRoster?>("_roster") == null ? "null" : "set")
            );
        }

        using (var lcu = new LcuStub(SlowRoute, slow: true))
        {
            var overlay = new Overlay(GameBarBackend);
            var poll = overlay.CallStart("PollAsync");
            Thread.Sleep(60);
            overlay.Dispose();
            PumpUntil(() => poll.IsCompleted);
            Check(
                overlay.Field<bool>("_disposed") && overlay.Field<LoadingRoster?>("_roster") == null,
                "Disposing during a poll leaves the manager shut down without publishing a roster"
            );
        }
    }

    private static LiveMatchSnapshot UnknownSnapshot() =>
        LiveMatchSnapshot.Parse(
            JObject.Parse(
                """
                {
                  "activePlayer": { "riotId": "未知#0000" },
                  "gameData": { "gameMode": "", "mapNumber": 0, "gameTime": 10 },
                  "allPlayers": [ { "riotId": "未知#0000", "team": "UNKNOWN", "level": 1 } ]
                }
                """
            )
        );

    #endregion

    #region Game Bar

    private static void WidgetFiles()
    {
        using var lcu = new LcuStub(LcuRoute);
        using var overlay = new Overlay(GameBarBackend);
        var roster = Roster();
        var live = Snapshot(true);
        overlay.SetPhase("InProgress");
        overlay.Call("ApplyRoster", roster, live);
        overlay.Call("UpdateSurvivalSounds", roster, Insight());
        overlay.Call("PublishGameBar");
        var snapshot = JObject.Parse(
            WaitForFile(Path.Combine(overlay.Folder, "snapshot.json"), "The Game Bar snapshot is written")
        );
        string session = (string)snapshot["session"]!;
        CheckSnapshot(snapshot);
        PumpUntil(() => overlay.Field<Dictionary<int, ControlSkill[]>>("_controls")!.ContainsKey(99));
        WriteWidgetFiles(overlay, session, roster, live);
        ReadDetail(overlay, session);
        ReadChat(overlay, session);
        Console.WriteLine("Game Bar snapshot, controls, respawn marks, augment and summoner files, details and chat.");
    }

    private static void CheckSnapshot(JObject snapshot)
    {
        Check(
            (int)snapshot["protocol"]! == 1 && (int)snapshot["liveProtocol"]! == 1,
            "The snapshot carries its protocols"
        );
        Check(
            (string?)snapshot["shortcutOwner"] == "main" && (string?)snapshot["shortcut"] == "",
            "No keyboard command is published to the widget"
        );
        var players = (JArray)snapshot["players"]!;
        Check(players.Count == 10, "Ten slots are published for the widget's columns");
        var self = players.OfType<JObject>().First(row => (bool?)row["self"] == true);
        Check(
            (int?)self["championId"] == 99 && ((string?)self["name"])!.Contains("自己"),
            "The player's own row carries the champion and the name: " + self["name"]
        );
        Check(
            ((string?)snapshot["teams"]![0]) == "我方" && (string?)snapshot["status"] != "",
            "The team labels and the status cross the bridge"
        );
        Check((bool?)snapshot["enabled"] == true && (bool?)snapshot["active"] == true, "An active match is published");
    }

    private static void WriteWidgetFiles(Overlay overlay, string session, LoadingRoster roster, LiveMatchSnapshot live)
    {
        File.WriteAllText(
            Path.Combine(overlay.Folder, "respawn-marks.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["session"] = session,
                ["slots"] = new JArray(0),
                ["heroes"] = new JArray(99),
                ["sound"] = false,
                ["test"] = -1,
            }.ToString(Formatting.None)
        );
        overlay.CallAsync("UpdateRespawnSoundsAsync", roster, live, overlay.Generation);
        Check(
            overlay.Field<object>("_respawnSounds") is RespawnMarkSounds,
            "Respawn marks are read through the bridge without playing a tone"
        );
        File.WriteAllText(
            Path.Combine(overlay.Folder, "augment-stacks.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["session"] = session,
                ["augments"] = new JArray(
                    new JObject
                    {
                        ["player"] = "",
                        ["augment"] = "InfiniteRecursion",
                        ["time"] = 30.0,
                        ["stacks"] = 4,
                        ["choice"] = "",
                    }
                ),
            }.ToString(Formatting.None)
        );
        File.WriteAllText(
            Path.Combine(overlay.Folder, "ultimate-haste.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["session"] = session,
                ["players"] = new JArray(new JObject { ["player"] = SelfPuuid, ["ultimateHaste"] = 25.0 }),
            }.ToString(Formatting.None)
        );
        overlay.CallAsync("ReadAugmentSelectionsAsync", overlay.Generation);
        Check(
            ((Array)overlay.Field<object>("_augmentSelections")!).Length == 1,
            "The widget's recorded augments are read"
        );
        Check(
            overlay.Field<IReadOnlyDictionary<string, double>>("_ultimateHaste")!.Count == 1,
            "The ultimate haste the user entered is read"
        );
        SummonerTimersFile(overlay, session);
        ControlSettingsFile(overlay);
    }

    private static void SummonerTimersFile(Overlay overlay, string session)
    {
        File.WriteAllText(
            Path.Combine(overlay.Folder, "summoner-timers.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["session"] = session,
                ["timers"] = new JArray(
                    new JObject
                    {
                        ["player"] = SelfPuuid,
                        ["slot"] = 0,
                        ["time"] = 120.0,
                    }
                ),
            }.ToString(Formatting.None)
        );
        overlay.CallAsync("ReadSummonerTimersAsync", overlay.Generation);
        var timers = (Array)overlay.Field<object>("_summonerTimers")!;
        Check(timers.Length == 1, "The ally timers the widget started are read");
        var summoners =
            (Dictionary<string, PlayerSummoner[]>)
                overlay.Call("DescribeSummoners", Roster(), Snapshot(false), Insight())!;
        Check(
            summoners[SelfPuuid].First(s => s.Slot == 0).Started == 120.0,
            "The running timer reaches the player's own summoner spell"
        );
    }

    private static void ControlSettingsFile(Overlay overlay)
    {
        File.WriteAllText(
            Path.Combine(overlay.Folder, "control-settings.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["survivalSound"] = false,
                ["allySound"] = true,
                ["summonerSound"] = false,
                ["cannonSound"] = CannonSoundModes.Off,
            }.ToString(Formatting.None)
        );
        overlay.CallAsync("ReadControlSettingsAsync", overlay.Generation);
        var settings = overlay.Field<ControlSettings>("_controlSettings")!;
        Check(
            settings.AllySound && !settings.EnemySound && !settings.SummonerSound,
            "The widget's sound choices are read; the temporary file keeps every tone silent"
        );
    }

    private static void ReadDetail(Overlay overlay, string session)
    {
        File.WriteAllText(
            Path.Combine(overlay.Folder, "detail-request.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString(),
                ["session"] = session,
                ["slot"] = 0,
                ["championId"] = 99,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }.ToString(Formatting.None)
        );
        overlay.CallAsync("ReadDetailSelection");
        var detail = JObject.Parse(
            WaitForFile(Path.Combine(overlay.Folder, "detail.json"), "The widget's detail reply is written")
        );
        Check(
            (int?)detail["championId"] == 99 && (string?)detail["session"] == session,
            "A roster selection is answered with that champion's detail"
        );
        Check(
            detail["sections"]!.Any(section => (string?)section["tab"] == "record"),
            "The reply carries the player's record tab: "
                + string.Join(",", detail["sections"]!.Select(s => (string?)s!["tab"]))
        );
        CancelledDetail(overlay, session);
    }

    /// <summary>A detail whose read is cancelled keeps the sections it already had.</summary>
    private static void CancelledDetail(Overlay overlay, string session)
    {
        var roster = overlay.Field<LoadingRoster>("_roster")!;
        var request = new OverlayDetailRequest(Guid.NewGuid().ToString(), session, 0, 99);
        string path = Path.Combine(overlay.Folder, "detail.json");
        File.Delete(path);
        overlay.Write("_lastDetailRequest", request.Id);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        overlay.CallAsync("LoadChampionDetail", request, roster.Players[0], roster, 0, "cancelled", cancelled.Token);
        var partial = JObject.Parse(
            WaitForFile(Path.Combine(overlay.Folder, "detail.json"), "The partial detail reply is written")
        );
        Check(
            (string?)partial["requestId"] == request.Id && (string?)partial["state"] == "partial",
            "A cancelled detail answers as partial instead of staying silent: " + partial["state"]
        );
    }

    private static void ReadChat(Overlay overlay, string session)
    {
        overlay.SetPhase("");
        File.WriteAllText(
            Path.Combine(overlay.Folder, "chat-request.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString(),
                ["session"] = session,
                ["target"] = "own",
                ["kind"] = "tier",
                ["slot"] = -1,
                ["championId"] = 0,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["dismissed"] = false,
                ["cancelled"] = false,
            }.ToString(Formatting.None)
        );
        overlay.CallAsync("ReadChatCommand");
        var status = JObject.Parse(
            WaitForFile(Path.Combine(overlay.Folder, "chat-status.json"), "The widget's chat reply is written")
        );
        Check(
            (string?)status["state"] == "phase" && ((string?)status["message"])!.Contains("进入游戏"),
            "Outside a running game a chat request is refused instead of typed: " + status["message"]
        );
        Check((bool?)status["complete"] == true, "The refusal is a complete answer to the widget");
        MoreDetailPaths(overlay, session);
        ChatPaths(overlay, session);
    }

    /// <summary>Clicks the widget can send that the overlay must not answer twice or at all.</summary>
    private static void MoreDetailPaths(Overlay overlay, string session)
    {
        string path = Path.Combine(overlay.Folder, "detail.json");
        string repeated = Guid.NewGuid().ToString();
        WriteDetailRequest(overlay, session, repeated, 0, 99);
        overlay.CallAsync("ReadDetailSelection");
        File.Delete(path);
        overlay.CallAsync("ReadDetailSelection");
        Check(!File.Exists(path), "The same detail click is not loaded a second time");
        WriteDetailRequest(overlay, session, Guid.NewGuid().ToString(), -1, 0);
        overlay.CallAsync("ReadDetailSelection");
        Check(!File.Exists(path), "A request without a player slot is ignored");
        overlay.Call(
            "ApplyRoster",
            Roster() with
            {
                Players = [Roster().Players[0] with { ChampionId = 25 }, .. Roster().Players.Skip(1)],
            },
            Snapshot(false)
        );
        WriteDetailRequest(overlay, session, Guid.NewGuid().ToString(), 0, 99);
        overlay.CallAsync("ReadDetailSelection");
        Check(!File.Exists(path), "A slot that changed champion since the click is not answered");
        AramDetail(overlay, session);
    }

    /// <summary>An ARAM detail falls back to the index when the builder page is missing.</summary>
    private static void AramDetail(Overlay overlay, string session)
    {
        var roster = overlay.Field<LoadingRoster>("_roster")!;
        var request = new OverlayDetailRequest(Guid.NewGuid().ToString(), session, 0, 99);
        string path = Path.Combine(overlay.Folder, "detail.json");
        File.Delete(path);
        overlay.Write("_lastDetailRequest", request.Id);
        overlay.CallAsync(
            "LoadChampionDetail",
            request,
            roster.Players[0],
            roster,
            2400,
            "none",
            CancellationToken.None
        );
        var detail = JObject.Parse(WaitForFile(path, "The statistics-index detail reply is written"));
        Check(
            (string?)detail["state"] == "partial" && ((string?)detail["source"])!.Contains("出装详情"),
            "A missing builder page is reported while the statistics still load: " + detail["source"]
        );
        Check(
            detail["sections"]!.Count(section => (string?)section["tab"] == "record") == 1,
            "The player's record tab is replaced on every reply, never duplicated"
        );
    }

    private static void WriteDetailRequest(Overlay overlay, string session, string id, int slot, int champion)
    {
        File.WriteAllText(
            Path.Combine(overlay.Folder, "detail-request.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["id"] = id,
                ["session"] = session,
                ["slot"] = slot,
                ["championId"] = champion,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }.ToString(Formatting.None)
        );
    }

    /// <summary>A cancelled click, and a click that cannot be sent from here.</summary>
    private static void ChatPaths(Overlay overlay, string session)
    {
        overlay.SetPhase("InProgress");
        string path = Path.Combine(overlay.Folder, "chat-status.json");
        File.Delete(path);
        WriteChatRequest(overlay, session, cancelled: true, ageMs: 0);
        overlay.CallAsync("ReadChatCommand");
        var cancelled = WaitChatStatus(overlay);
        Check((string?)cancelled["state"] == "cancelled", "A click the widget cancelled is never sent");
        File.Delete(path);
        WriteChatRequest(overlay, session, cancelled: false, ageMs: 19000);
        overlay.CallAsync("ReadChatCommand");
        var refused = WaitChatStatus(overlay);
        Check(
            (string?)refused["state"] is "permission" or "unavailable" or "timeout",
            "A click that cannot be sent is refused with a reason instead of typed: " + refused["message"]
        );
    }

    private static void WriteChatRequest(Overlay overlay, string session, bool cancelled, long ageMs)
    {
        File.WriteAllText(
            Path.Combine(overlay.Folder, "chat-request.json"),
            new JObject
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString(),
                ["session"] = session,
                ["target"] = "own",
                ["kind"] = "tier",
                ["slot"] = -1,
                ["championId"] = 0,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ageMs,
                ["dismissed"] = false,
                ["cancelled"] = cancelled,
            }.ToString(Formatting.None)
        );
    }

    /// <summary>The widget's complete answer, not the progress report written before it.</summary>
    private static JObject WaitChatStatus(Overlay overlay)
    {
        string path = Path.Combine(overlay.Folder, "chat-status.json");
        JObject? status = null;
        PumpUntil(() =>
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var value = JObject.Parse(File.ReadAllText(path));
                status = (bool?)value["complete"] == true ? value : null;
                return status != null;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        });
        return status!;
    }

    #endregion

    #region 固定数据

    private static LoadingRoster Roster() => LoadingRoster.Parse(Flow(), SelfPuuid);

    private static LiveMatchSnapshot Snapshot(bool dead) => LiveMatchSnapshot.Parse(JObject.Parse(LiveBody(dead)));

    private static ControlSettings Silent() =>
        new(false, false) { SummonerSound = false, CannonSound = CannonSoundModes.Off };

    private static JObject Flow() =>
        new()
        {
            ["gameData"] = new JObject
            {
                ["gameId"] = "555",
                ["queue"] = new JObject { ["id"] = 0, ["gameMode"] = "" },
                ["teamOne"] = new JArray(
                    RosterRow(SelfPuuid, "自己", 11, 99, "0001"),
                    RosterRow("ally-puuid", "队友", 12, 25, "0002")
                ),
                ["teamTwo"] = new JArray(RosterRow("enemy-puuid", "对手", 13, 1, "0003")),
                ["playerChampionSelections"] = new JArray(
                    new JObject { ["puuid"] = SelfPuuid, ["championId"] = 99 },
                    new JObject { ["puuid"] = "ally-puuid", ["championId"] = 25 },
                    new JObject { ["puuid"] = "enemy-puuid", ["championId"] = 1 }
                ),
            },
        };

    /// <summary>The same session with the player's own account listed twice, as an earlier game leaves it.</summary>
    private static JObject ConflictFlow()
    {
        var flow = Flow();
        ((JArray)flow["gameData"]!["teamOne"]!).Add(RosterRow(SelfPuuid, "自己", 11, 25, "0001"));
        return flow;
    }

    private static JObject RosterRow(string puuid, string name, long id, int champion, string tag) =>
        new()
        {
            ["puuid"] = puuid,
            ["summonerId"] = id,
            ["gameName"] = name,
            ["tagLine"] = tag,
            ["championId"] = champion,
        };

    private static string LiveBody(bool dead, string mode = "") =>
        new JObject
        {
            ["activePlayer"] = new JObject { ["riotId"] = "自己#0001", ["summonerName"] = "自己" },
            ["gameData"] = new JObject
            {
                ["gameMode"] = mode,
                ["mapNumber"] = 12,
                ["gameTime"] = 200,
            },
            ["allPlayers"] = new JArray(
                LiveRow("自己#0001", "自己", "ORDER", 11, dead, dead ? 3.0 : 0),
                LiveRow("队友#0002", "队友", "ORDER", 9, false, 0),
                LiveRow("对手#0003", "对手", "CHAOS", 10, false, 0)
            ),
            ["events"] = new JObject { ["Events"] = Events() },
        }.ToString(Formatting.None);

    private static JObject LiveRow(string riotId, string name, string team, int level, bool dead, double respawn) =>
        new()
        {
            ["riotId"] = riotId,
            ["summonerName"] = name,
            ["team"] = team,
            ["rawChampionName"] = "game_character_displayname_Lux",
            ["championName"] = "拉克丝",
            ["level"] = level,
            ["isDead"] = dead,
            ["respawnTimer"] = respawn,
            ["scores"] = new JObject
            {
                ["kills"] = 3,
                ["deaths"] = 2,
                ["assists"] = 5,
            },
            ["items"] = new JArray(
                new JObject
                {
                    ["itemID"] = 1001,
                    ["displayName"] = "鞋子",
                    ["count"] = 1,
                    ["price"] = 300,
                }
            ),
            ["summonerSpells"] = new JObject
            {
                ["summonerSpellOne"] = new JObject
                {
                    ["rawDisplayName"] = "GeneratedTip_SummonerSpell_SummonerFlash_DisplayName",
                },
                ["summonerSpellTwo"] = new JObject
                {
                    ["rawDisplayName"] = "GeneratedTip_SummonerSpell_SummonerHeal_DisplayName",
                },
            },
        };

    private static JArray Events()
    {
        JObject Kill(int id, string killer, string victim) =>
            new()
            {
                ["EventID"] = id,
                ["EventName"] = "ChampionKill",
                ["EventTime"] = 30 + id,
                ["KillerName"] = killer,
                ["VictimName"] = victim,
            };
        return new JArray(
            new JObject
            {
                ["EventID"] = 0,
                ["EventName"] = "GameStart",
                ["EventTime"] = 0,
            },
            Kill(1, "对手", "自己"),
            Kill(2, "自己", "对手"),
            Kill(3, "队友", "对手"),
            new JObject
            {
                ["EventID"] = 4,
                ["EventName"] = "Multikill",
                ["EventTime"] = 33,
                ["KillerName"] = "队友",
                ["KillStreak"] = 2,
            },
            new JObject
            {
                ["EventID"] = 5,
                ["EventName"] = "Ace",
                ["EventTime"] = 34,
                ["AcingTeam"] = "ORDER",
            }
        );
    }

    #endregion

    #region 桩
    /// <summary>The client's answers, including a broken statistics index for the champion detail service.</summary>
    private static HttpResponseMessage LcuRoute(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;
        return path switch
        {
            "/lol-gameflow/v1/gameflow-phase" => Json("\"InProgress\""),
            "/lol-gameflow/v1/session" => Json(Flow().ToString(Formatting.None)),
            "/lol-summoner/v1/current-summoner" => Json(
                new JObject { ["puuid"] = SelfPuuid }.ToString(Formatting.None)
            ),
            "/lol-perks/v1/currentpage" => Json(
                new JObject { ["selectedPerkIds"] = new JArray(8347, 8000) }.ToString(Formatting.None)
            ),
            "/lol-game-data/assets/v1/champions/99.json" => Json(Champion().ToString(Formatting.None)),
            _ => path.StartsWith("/lol-summoner/", StringComparison.Ordinal)
                ? Json(
                    new JObject
                    {
                        ["puuid"] = SelfPuuid,
                        ["summonerId"] = 11,
                        ["gameName"] = "自己",
                        ["tagLine"] = "0001",
                    }.ToString(Formatting.None)
                )
                : Missing(),
        };
    }

    /// <summary>A phase read that arrives late, so a poll can be cancelled or disposed while it waits.</summary>
    private static HttpResponseMessage SlowRoute(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/lol-gameflow/v1/gameflow-phase"
            ? Json("\"InProgress\"")
            : LcuRoute(request);

    /// <summary>The client sitting in a lobby: a phase outside a game, without a session to read.</summary>
    private static HttpResponseMessage LobbyRoute(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/lol-gameflow/v1/gameflow-phase" ? Json("\"Lobby\"") : Missing();

    /// <summary>A session that lists the player's own account twice and has no queue node.</summary>
    private static HttpResponseMessage ConflictRoute(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/lol-gameflow/v1/gameflow-phase")
        {
            return Json("\"InProgress\"");
        }

        if (path != "/lol-gameflow/v1/session")
        {
            return LcuRoute(request);
        }

        var flow = ConflictFlow();
        ((JObject)flow["gameData"]!).Remove("queue");
        return Json(flow.ToString(Formatting.None));
    }

    /// <summary>A session with no game data at all; the roster comes from the live client alone.</summary>
    private static HttpResponseMessage BareRoute(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/lol-gameflow/v1/session"
            ? Json(new JObject().ToString(Formatting.None))
            : LcuRoute(request);

    private static JObject Champion() =>
        new()
        {
            ["id"] = 99,
            ["alias"] = "Lux",
            ["name"] = "拉克丝",
            ["spells"] = new JArray(
                new JObject
                {
                    ["spellKey"] = "q",
                    ["name"] = "光之束缚",
                    ["description"] = "拉克丝放出光球，禁锢命中的敌人。",
                },
                new JObject
                {
                    ["spellKey"] = "w",
                    ["name"] = "曲光屏障",
                    ["description"] = "拉克丝抛出法杖保护自己。",
                }
            ),
        };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Missing() =>
        new(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }
    }

    /// <summary>Answers after a delay: the phase read ignores cancellation, the session read honours it.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public SlowHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool phase = request.RequestUri!.AbsolutePath.EndsWith("gameflow-phase", StringComparison.Ordinal);
            await Task.Delay(250, phase ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            return _respond(request);
        }
    }

    /// <summary>The process-wide LCU session, replaced for the duration of one check and restored afterwards.</summary>
    private sealed class LcuStub : IDisposable
    {
        private readonly LcuSession _original;
        private readonly HttpClient? _http;

        /// <param name="respond">The client's answers; null connects no client at all.</param>
        /// <param name="slow">Whether the client answers late, so the poll can be interrupted.</param>
        public LcuStub(Func<HttpRequestMessage, HttpResponseMessage>? respond, bool slow = false)
        {
            _original = League.FormMain.Globals.lcuClient;
            var session = new LcuSession();
            if (respond != null)
            {
                _http = new HttpClient(slow ? new SlowHandler(respond) : new StubHandler(respond))
                {
                    BaseAddress = new Uri("https://127.0.0.1:1/"),
                };
                var client = new LcuClient("1", "stub");
                var replaced = client.HttpClient;
                typeof(LcuClient).GetField("_httpClient", Flags)!.SetValue(client, _http);
                typeof(LcuSession).GetField("_lcuClient", Flags)!.SetValue(session, client);
                replaced.Dispose();
            }

            League.FormMain.Globals.lcuClient = session;
        }

        public void Dispose()
        {
            League.FormMain.Globals.lcuClient = _original;
            _http?.Dispose();
        }
    }

    #endregion

    #region 夹具

    /// <summary>One manager on its own window with every remote client and every sound replaced.</summary>
    private sealed class Overlay : IDisposable
    {
        private readonly IDisposable? _hotkeys;
        private Func<string> _live = () => LiveBody(true);
        private Action? _liveHook;
        private bool _disposed;

        public Overlay(string backend)
        {
            Folder = Path.Combine(Path.GetTempPath(), "league-overlay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            WriteFixedData();
            Owner = new League.FormMain();
            _hotkeys = ReadField(Owner, "_hotkeyManager") as IDisposable;
            Manager = ReadField(Owner, "_loadingOverlay")!;
            Type = Manager.GetType();
            StopTimers();
            Config = new LeagueConfig { LoadingOverlayBackend = backend };
            WriteField(Owner, "_appConfig", Config);
            // No chat sender means no keystroke can ever be typed by this check.
            WriteField(Owner, "_chatSender", null);
            Bridge = new GameBarBridge(Folder);
            Replace("_gameBar", Bridge);
            Replace(
                "_live",
                new LiveMatchClient(
                    new StubHandler(_ =>
                    {
                        _liveHook?.Invoke();
                        return Json(_live());
                    })
                )
            );
            Replace("_controlSettings", Silent());
            Replace("_respawnSounds", new RespawnMarkSounds(_ => { }));
            Replace("_itemSounds", new ItemSoundLibrary(Folder));
            Replace("_baseStatsData", new ChampionBaseStatsData(Folder, "16.20"));
            Replace("_summonerData", new SummonerSpellData(Folder, "16.20"));
            Replace("_spellData", new ChampionSpellData(Folder, "16.20"));
            Replace("_championDetails", OfflineDetails());
        }

        public League.FormMain Owner { get; }

        public object Manager { get; }

        public Type Type { get; }

        public string Folder { get; }

        public GameBarBridge Bridge { get; }

        public LeagueConfig Config { get; }

        public LoadingOverlayDisplayState Display => Field<LoadingOverlayDisplayState>("_display");

        public Form? Window => Field<object?>("_window") as Form;

        public OverlayLiveState? LiveState => Field<OverlayLiveState?>("_liveState");

        public int Generation => Field<int>("_generation");

        public string Status() => (string)Type.GetProperty("GameBarStatus")!.GetValue(Manager)!;

        public T Field<T>(string name) => (T)ReadField(Manager, name)!;

        public void Write(string name, object? value) => WriteField(Manager, name, value);

        public object? Call(string name, params object?[] arguments) => Invoke(Manager, name, arguments);

        /// <summary>Starts a private asynchronous method without waiting for it.</summary>
        public Task CallStart(string name, params object?[] arguments) => (Task)Invoke(Manager, name, arguments)!;

        public void CallAsync(string name, params object?[] arguments)
        {
            var task = CallStart(name, arguments);
            PumpUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        public T CallAsyncResult<T>(string name, params object?[] arguments)
        {
            var task = (Task<T>)Invoke(Manager, name, arguments)!;
            PumpUntil(() => task.IsCompleted);
            return task.GetAwaiter().GetResult();
        }

        /// <summary>Raises a timer's tick once, the same way the message loop would.</summary>
        public void Tick(string timer)
        {
            var instance = ReadField(Manager, timer)!;
            var handler =
                instance
                    .GetType()
                    .GetFields(Flags)
                    .First(field => field.FieldType == typeof(EventHandler))
                    .GetValue(instance) as EventHandler;
            handler?.Invoke(instance, EventArgs.Empty);
        }

        public void FailLive() => Replace("_live", new LiveMatchClient(new StubHandler(_ => Missing())));

        public void SetPhase(string phase)
        {
            Write("_phase", phase);
            Write("_lastPhaseRead", Environment.TickCount64);
        }

        public void SetLive(string body) => _live = () => body;

        /// <summary>Runs while the live client is being read, so a poll can be interrupted inside itself.</summary>
        public void SetLiveHook(Action hook) => _liveHook = hook;

        public void CancelLife() => Field<CancellationTokenSource>("_life").Cancel();

        public void StopTimers()
        {
            foreach (string name in new[] { "_poll", "_follow", "_paint", "_detailPoll" })
            {
                ((System.Windows.Forms.Timer)ReadField(Manager, name)!).Stop();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Invoke(Manager, "Dispose", []);
            Bridge.Dispose();
            Owner.Dispose();
            _hotkeys?.Dispose();
            try
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine("Overlay folder kept: " + Folder);
            }
        }

        private void Replace(string name, object? value)
        {
            (ReadField(Manager, name) as IDisposable)?.Dispose();
            WriteField(Manager, name, value);
        }

        /// <summary>Champion statistics, summoner spells and spell facts, all cached in the temporary folder.</summary>
        private void WriteFixedData()
        {
            File.WriteAllText(
                Path.Combine(Folder, "16.20.json"),
                new JObject
                {
                    ["data"] = new JObject
                    {
                        ["Lux"] = ChampionStats("99"),
                        ["Morgana"] = ChampionStats("25"),
                        ["Annie"] = ChampionStats("1"),
                    },
                }.ToString(Formatting.None)
            );
            string spells = Path.Combine(Folder, "16.20");
            Directory.CreateDirectory(spells);
            File.WriteAllText(
                Path.Combine(spells, "summoner.json"),
                new JObject
                {
                    ["data"] = new JObject
                    {
                        ["SummonerFlash"] = Spell("闪现", 300, "flash.png"),
                        ["SummonerHeal"] = Spell("治疗术", 240, "heal.png"),
                    },
                }.ToString(Formatting.None)
            );
            File.WriteAllText(
                Path.Combine(spells, "modes-r1.json"),
                new JObject { ["SummonerHeal"] = new JObject { ["aram"] = 100.0 } }.ToString(Formatting.None)
            );
            WriteIcon(Path.Combine(spells, "flash.png"));
            WriteIcon(Path.Combine(spells, "heal.png"));
            string championSpells = Path.Combine(Folder, "16.20-r" + ChampionSpellData.RulesVersion);
            Directory.CreateDirectory(championSpells);
            File.WriteAllText(
                Path.Combine(championSpells, "lux.json"),
                new JObject
                {
                    ["Slows"] = new JArray(
                        new JObject
                        {
                            ["Key"] = "Q",
                            ["Percent"] = 70,
                            ["Range"] = 900,
                            ["Duration"] = 2.0,
                        }
                    ),
                    ["Cooldowns"] = new JArray(
                        new JObject
                        {
                            ["Key"] = "Q",
                            ["Name"] = "光之束缚",
                            ["Cooldowns"] = new JArray(10, 9, 8, 7, 6),
                        }
                    ),
                }.ToString(Formatting.None)
            );
            WriteEmptyAndBroken();
        }

        /// <summary>Statistics files that hold nothing, and files that cannot be read at all.</summary>
        private void WriteEmptyAndBroken()
        {
            string emptySpells = Path.Combine(Folder, "empty", "16.20"),
                brokenSpells = Path.Combine(Folder, "bad", "16.20");
            Directory.CreateDirectory(emptySpells);
            Directory.CreateDirectory(brokenSpells);
            File.WriteAllText(Path.Combine(Folder, "empty", "16.20.json"), "{\"data\":{}}");
            File.WriteAllText(Path.Combine(Folder, "bad", "16.20.json"), "not json");
            File.WriteAllText(Path.Combine(emptySpells, "summoner.json"), "{\"data\":{}}");
            File.WriteAllText(Path.Combine(emptySpells, "modes-r1.json"), "{}");
            File.WriteAllText(Path.Combine(brokenSpells, "summoner.json"), "not json");
            File.WriteAllText(Path.Combine(brokenSpells, "modes-r1.json"), "not json");
        }

        private static JObject ChampionStats(string key) =>
            new()
            {
                ["key"] = key,
                ["stats"] = new JObject
                {
                    ["hp"] = 580,
                    ["hpperlevel"] = 99,
                    ["armor"] = 21,
                    ["armorperlevel"] = 4,
                    ["spellblock"] = 30,
                    ["spellblockperlevel"] = 1.3,
                    ["attackspeedperlevel"] = 2,
                    ["attackrange"] = 550,
                },
            };

        private static JObject Spell(string name, int cooldown, string image) =>
            new()
            {
                ["name"] = name,
                ["cooldown"] = new JArray(cooldown),
                ["image"] = new JObject { ["full"] = image },
            };

        private static OverlayChampionDetailService OfflineDetails()
        {
            var details = new OverlayChampionDetailService(() => TimeSpan.FromMinutes(1));
            var field = typeof(OverlayChampionDetailService).GetField("_am", Flags)!;
            (field.GetValue(details) as IDisposable)?.Dispose();
            field.SetValue(
                details,
                new AramMetaClient(
                    new StubHandler(request =>
                        request.RequestUri!.AbsolutePath.EndsWith("tier-list.json", StringComparison.Ordinal)
                            ? Json(AramIndex().ToString(Formatting.None))
                            : Missing()
                    ),
                    cachePath: null
                )
            );
            return details;
        }

        /// <summary>A small but valid statistics index, so the damage mix and the composition can be built.</summary>
        private static JObject AramIndex() =>
            new()
            {
                ["patch_prefix"] = "16.20",
                ["detailVersion"] = "v1",
                ["augs"] = new JObject(),
                ["champs"] = new JObject
                {
                    ["99"] = ChampionRow("拉克丝", "Lux", ["Mage"], 0.53),
                    ["25"] = ChampionRow("莫甘娜", "Morgana", ["Mage"], 0.5),
                    ["1"] = ChampionRow("黑暗之女", "Annie", ["Mage"], 0.49),
                },
            };

        private static JObject ChampionRow(string name, string alias, string[] tags, double winRate) =>
            new()
            {
                ["name_zh"] = name,
                ["alias"] = alias,
                ["tags"] = new JArray(tags),
                ["wr"] = winRate,
                ["rawWr"] = winRate,
                ["g"] = 1000,
            };
    }

    private static object? ReadField(object target, string name) =>
        target.GetType().GetField(name, Flags)!.GetValue(target);

    private static void WriteIcon(string path)
    {
        using var bitmap = new Bitmap(4, 4);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllBytes(path, stream.ToArray());
    }

    private static void WriteField(object target, string name, object? value) =>
        target.GetType().GetField(name, Flags)!.SetValue(target, value);

    private static MethodInfo Method(object target, string name, int parameters) =>
        target
            .GetType()
            .GetMethods(Flags | BindingFlags.Public)
            .Single(m => m.Name == name && m.GetParameters().Length == parameters);

    /// <summary>Calls a private method, filling in optional parameters the caller leaves out.</summary>
    private static object? Invoke(object target, string name, object?[] arguments)
    {
        var methods = target.GetType().GetMethods(Flags | BindingFlags.Public).Where(m => m.Name == name).ToArray();
        var method =
            methods.FirstOrDefault(m => m.GetParameters().Length == arguments.Length)
            ?? methods.Single(m =>
                m.GetParameters().Length > arguments.Length
                && m.GetParameters().Skip(arguments.Length).All(p => p.HasDefaultValue)
            );
        return method.Invoke(
            target,
            arguments.Concat(method.GetParameters().Skip(arguments.Length).Select(_ => Type.Missing)).ToArray()
        );
    }

    private static string WaitForFile(string path, string expectation)
    {
        var task = BridgeCheckFiles.ReadAsync(path, expectation);
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    #endregion
}
