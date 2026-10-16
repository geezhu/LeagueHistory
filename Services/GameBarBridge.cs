using System.Drawing.Imaging;
using System.Threading.Channels;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>Local snapshots and bounded roster commands. No LCU credentials or network endpoints cross the boundary.</summary>
public sealed partial class GameBarBridge : IDisposable
{
    private readonly Channel<JObject> _pending = Channel.CreateBounded<JObject>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
    );
    private readonly Dictionary<int, string> _images = new();
    private readonly string? _testDirectory;
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private string _lastCommand = "",
        _command = "",
        _directory = "";
    private long _revision,
        _nextDiscovery;
    private bool _disposed;
    public string Status { get; private set; } = "正在检查 Game Bar 组件…";
    public Task Completion { get; }

    public GameBarBridge(string? directory = null)
    {
        _testDirectory = directory;
        Completion = Task.WhenAll(Task.Run(WriteLoop), Task.Run(WriteDetailLoop), Task.Run(WriteChatLoop));
    }

    public void Publish(
        LoadingRoster? roster,
        IReadOnlyDictionary<string, PlayerChampionTier> ranks,
        IReadOnlyDictionary<int, Image> icons,
        Func<int, string> champion,
        Func<LoadingPlayer, string> details,
        string session,
        string status,
        bool enabled,
        bool active,
        bool show,
        IReadOnlyDictionary<string, PlayerChampionMastery>? masteries = null,
        IReadOnlyDictionary<string, LiveDeathStreak>? deathStreaks = null,
        IReadOnlyDictionary<string, OverlayPartyMarker>? parties = null,
        OverlayLiveState? live = null,
        IReadOnlyDictionary<int, ControlSkill[]>? controls = null,
        IReadOnlyDictionary<int, SpellCooldown[]>? cooldowns = null,
        JObject? composition = null
    )
    {
        if (_disposed)
        {
            return;
        }

        string identity = $"{session}|{enabled}|{active}|{show}";
        if (identity != _lastCommand)
        {
            _lastCommand = identity;
            _command = Guid.NewGuid().ToString("N");
        }
        int[] teams = Teams(roster);
        var rows = new JArray();
        foreach (var p in enabled && active ? Slots(roster) : new LoadingPlayer?[10])
        {
            if (p == null)
            {
                rows.Add(new JObject());
                continue;
            }
            var row = PlayerRow(p, ranks, icons, champion, details, masteries, deathStreaks, parties);
            AddLiveFields(row, p, live);
            var spells = cooldowns?.GetValueOrDefault(p.ChampionId);
            AddControls(row, p, controls, spells, live);
            AddCalculator(row, p, spells, live);
            rows.Add(row);
        }
        _detailScope = new(
            enabled && active ? _instance + ":" + session : "",
            rows.Select(r => (int?)r["championId"] ?? 0).ToArray()
        );
        var snapshot = new JObject
        {
            ["detailProtocol"] = 1,
            ["chatProtocol"] = 1,
            ["chatMastery"] = true,
            ["chatDeathStreak"] = true,
            ["chatWaves"] = true,
            ["chatRecord"] = true,
            ["chatParty"] = true,
            ["chatControls"] = true,
            ["chatComposition"] = true,
            ["compositionProtocol"] = 1,
            ["controlProtocol"] = 1,
            ["chatVoice"] = true,
            ["voiceProtocol"] = 2,
            ["chatTeams"] = roster?.Players.Any(p => p.Self) ?? false,
            // Right-clicking an ally's summoner spell sends its timer.
            ["chatSummoner"] = true,
            ["queue"] = roster?.Queue ?? 0,
            ["protocol"] = 1,
            ["revision"] = _instance + ":" + (++_revision),
            ["command"] = _instance + ":" + _command,
            ["session"] = _instance + ":" + session,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["enabled"] = enabled,
            ["active"] = active,
            ["show"] = show,
            // Prevent older widgets from falling back to their built-in keyboard watcher.
            ["shortcutOwner"] = "main",
            ["shortcut"] = "",
            ["status"] = status,
            ["teams"] = new JArray(teams.Select(t => roster?.TeamLabel(t) ?? (t == 100 ? "我方" : "对方"))),
            ["players"] = rows,
        };
        // Hidden or inactive, the widget gets the live fields empty.
        AddLiveSnapshot(snapshot, enabled && active ? live : null);
        // Present only for ARAM-type games once arammeta's statistics are read; the widget hides the page otherwise.
        if (composition != null && enabled && active)
        {
            snapshot["composition"] = composition.DeepClone();
        }

        _pending.Writer.TryWrite(snapshot);
    }

    private static void AddLiveSnapshot(JObject snapshot, OverlayLiveState? live)
    {
        snapshot["liveProtocol"] = 1;
        snapshot["teamSummaries"] = new JArray(live != null ? TeamSummaries(live.Insight) : ["", ""]);
        snapshot["liveRevision"] = live?.Revision ?? "";
        snapshot["liveSections"] = live?.Sections.DeepClone() ?? new JArray();
        snapshot["waves"] = live?.Waves is { } waves ? waves.ToJson() : null;
        // 2: the augment page with the catalog, choices and hand-entered ultimate haste.
        snapshot["augmentProtocol"] = 2;
        snapshot["augmentCatalog"] = AugmentCatalogJson();
        snapshot["ultimateHaste"] = new JObject(
            (live?.UltimateHaste ?? new Dictionary<string, double>()).Select(entry => new JProperty(
                entry.Key,
                entry.Value
            ))
        );
        snapshot["augmentStacks"] = live != null ? AugmentJson(live.AugmentStacks, live.Effects) : new JArray();
        snapshot["summonerIcons"] = SummonerIconsJson(live);
        // The game clock for the ally summoner timers; the wave clock is missing in modes without waves.
        snapshot["gameClock"] =
            live != null ? new JObject { ["gameTime"] = live.GameTime, ["clock"] = live.GameTimeAt } : null;
    }

    private static int[] Teams(LoadingRoster? roster) =>
        roster?.Players.FirstOrDefault(p => p.Self)?.Team == 200 ? [200, 100] : [100, 200];

    /// <summary>The widget's ten slots: own team first, five per side; empty seats are null.</summary>
    public static LoadingPlayer?[] Slots(LoadingRoster? roster) =>
        Teams(roster)
            .SelectMany(team =>
            {
                var players = roster?.Players.Where(p => p.Team == team).Take(5).ToArray() ?? [];
                return Enumerable.Range(0, 5).Select(i => players.ElementAtOrDefault(i));
            })
            .ToArray();

    // Own team first, matching the roster columns.
    private static string[] TeamSummaries(LiveMatchInsight insight) =>
        [
            $"{insight.Own.Kills} 杀 · {LivePanelSections.Gold(insight.Own.ItemGold)} · {LivePanelSections.Lead(insight.GoldLead)}",
            $"{insight.Enemy.Kills} 杀 · {LivePanelSections.Gold(insight.Enemy.ItemGold)}",
        ];

    /// <summary>Respawn time counts down in the widget from the snapshot time; scores replace the loading statistics.</summary>
    private static void AddLiveFields(JObject row, LoadingPlayer player, OverlayLiveState? live)
    {
        row["self"] = player.Self;
        if (live?.Insight.Players.GetValueOrDefault(player.Key) is not { } insight)
        {
            return;
        }

        row["respawn"] = Math.Round(insight.Respawn, 1);
        if (live.Health.GetValueOrDefault(player.Key) is { } health)
        {
            row["health"] = health.Text;
            row["healthDetails"] = health.Details;
        }

        row["survival"] = new JArray(
            SurvivalItems
                .Find(insight.Items)
                .Select(item => new JObject
                {
                    ["name"] = item.Name,
                    ["effect"] = item.Effect,
                    ["rule"] = item.Rule,
                })
        );
        AddSummonerFields(row, player.Key, live);
        // The Tab scoreboard already shows KDA; the card adds what it does not: participation and item value.
        row["liveStats"] =
            (insight.KillParticipation is double kp ? "参团 " + LivePanelSections.Percent(kp) + " · " : "")
            + "装备 "
            + LivePanelSections.Gold(insight.ItemGold);
    }

    private JObject PlayerRow(
        LoadingPlayer p,
        IReadOnlyDictionary<string, PlayerChampionTier> ranks,
        IReadOnlyDictionary<int, Image> icons,
        Func<int, string> champion,
        Func<LoadingPlayer, string> details,
        IReadOnlyDictionary<string, PlayerChampionMastery>? masteries,
        IReadOnlyDictionary<string, LiveDeathStreak>? deathStreaks,
        IReadOnlyDictionary<string, OverlayPartyMarker>? parties
    )
    {
        if (!_images.ContainsKey(p.ChampionId) && icons.TryGetValue(p.ChampionId, out var icon))
        {
            using var small = new Bitmap(icon, new Size(48, 48));
            using var buffer = new MemoryStream();
            small.Save(buffer, ImageFormat.Png);
            _images[p.ChampionId] = Convert.ToBase64String(buffer.ToArray());
        }
        var rank = ranks.GetValueOrDefault(p.Key);
        var mastery = masteries?.GetValueOrDefault(p.Key);
        if (mastery?.ChampionId != p.ChampionId)
        {
            mastery = null;
        }

        var streak = deathStreaks?.GetValueOrDefault(p.Key) ?? LiveDeathStreak.Unavailable();
        var party = p.IdentityHidden ? null : parties?.GetValueOrDefault(p.Key);
        if (party?.Size is not (>= 2 and <= 5))
        {
            party = null;
        }

        return new JObject
        {
            ["key"] = p.Key,
            ["championId"] = p.ChampionId,
            ["champion"] = champion(p.ChampionId),
            ["name"] = p.Name + (p.Self ? " · 自己" : ""),
            ["tier"] = rank?.Tier ?? "—",
            ["color"] = AramMetaView.TierColor(rank?.Tier ?? "—").ToArgb() & 0xffffff,
            ["stats"] = rank == null ? "统计读取中…" : rank.Rate + " · 选用率 " + rank.PickRate,
            ["mastery"] = p.ChampionId > 0 ? mastery?.Text ?? "成就读取中…" : "等待英雄确认",
            ["masteryDetails"] = mastery?.Description ?? "正在读取该玩家使用此英雄的成就。",
            ["deathStreak"] = p.ChampionId > 0 ? streak.Text : "",
            ["deathStreakDetails"] = streak.Rule,
            ["currentDeathStreak"] = streak.Current,
            ["maxDeathStreak"] = streak.Highest,
            ["deathStreakComplete"] = streak.Complete,
            ["party"] = party?.Badge ?? "",
            ["partySize"] = party?.Size ?? 0,
            ["partyLabel"] = party?.Label ?? "",
            ["partyColor"] = party?.Color ?? 0,
            ["partyDetails"] = party?.Description ?? "",
            ["details"] = champion(p.ChampionId) + " · " + details(p) + "\n" + (rank?.Note ?? "英雄统计尚未读取。"),
            ["icon"] = _images.GetValueOrDefault(p.ChampionId) ?? "",
        };
    }

    private string FindDirectory()
    {
        if (_testDirectory != null)
        {
            return _testDirectory;
        }

        if (Environment.TickCount64 < _nextDiscovery)
        {
            return _directory;
        }

        _nextDiscovery = Environment.TickCount64 + 10000;
        string packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages"
        );
        _directory = Directory.Exists(packages)
            ? Directory
                .EnumerateDirectories(packages, "LeagueHistory.GameBar_*")
                .Select(p => Path.Combine(p, "LocalState"))
                .FirstOrDefault(Directory.Exists)
                ?? ""
            : "";
        return _directory;
    }

    private async Task WriteLoop()
    {
        await foreach (var data in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                string directory = FindDirectory();
                if (directory.Length == 0)
                {
                    Status = "组件尚未就绪 · 安装后按 Win＋G 打开";
                    continue;
                }
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "snapshot.json"),
                    temporary = Path.Combine(directory, "snapshot-" + _instance + ".tmp");
                try
                {
                    await File.WriteAllTextAsync(
                            temporary,
                            data.ToString(Newtonsoft.Json.Formatting.None),
                            new System.Text.UTF8Encoding(false)
                        )
                        .ConfigureAwait(false);
                    File.Move(temporary, path, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                Status = DescribeWidgetState(null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                string heartbeat = Path.Combine(directory, "widget-status.json");
                if (File.Exists(heartbeat) && new FileInfo(heartbeat).Length < 8192)
                {
                    var state = JObject.Parse(await File.ReadAllTextAsync(heartbeat).ConfigureAwait(false));
                    Status = DescribeWidgetState(state, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
            {
                Status = "本机同步不可用：" + e.Message;
            }
        }
    }

    public static string DescribeWidgetState(JObject? state, long now)
    {
        long age = now - ((long?)state?["updatedAt"] ?? 0);
        if (state == null || age < 0 || age >= 15000)
        {
            return "组件未连接 · 按 Win＋G 打开「League 对局速览」";
        }

        string error = (string?)state["error"] ?? "";
        if (error.Length > 0)
        {
            return error;
        }

        if (
            (bool?)state["connected"] == false
            || (state["connected"] == null && !string.IsNullOrEmpty((string?)state["readError"]))
        )
        {
            return "组件已打开，但尚未读到有效数据";
        }

        if ((double?)state["width"] <= 0 || (double?)state["height"] <= 0)
        {
            return "组件画面尚未加载，请关闭后重新打开";
        }

        if ((bool?)state["pinned"] != true)
        {
            return "已连接，尚未固定 · 点图钉可留在游戏上方";
        }

        if ((bool?)state["active"] == false)
        {
            return "已连接并固定 · 等待对局";
        }

        return "已连接并固定";
    }

    private readonly GameBarVoiceRecognition _voiceRecognition = new();

    public Task PollVoiceAsync(bool enabled, CancellationToken token) =>
        _voiceRecognition.PollAsync(_testDirectory ?? _directory, _detailScope.Session, enabled && !_disposed, token);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _voiceRecognition.Dispose();
        _pending.Writer.TryComplete();
        _detailPending.Writer.TryComplete();
        _chatPending.Writer.TryComplete();
    }
}
