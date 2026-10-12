using League.Models;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private readonly Button _rosterButton = new()
    {
        Text = "可选英雄",
        Dock = DockStyle.Right,
        Width = Dip(112),
        FlatStyle = FlatStyle.Flat,
        Enabled = false,
        Visible = false,
    };
    private ChampionSelectRoster? _roster;
    private int _rosterQueue;
    private Form? _rosterWindow;
    private Action? _rosterChanged;

    private void UpdateRoster(JObject session, int queue)
    {
        if (_rosterQueue != queue)
        {
            _rosterWindow?.Close();
        }

        _rosterQueue = queue;
        bool supported =
            queue is 450 or 2400 || (bool?)session["benchEnabled"] == true || SelectedMode is "aram" or "mayhem";
        if (!supported)
        {
            ClearRoster();
            return;
        }
        var next = ChampionSelectRoster.Parse(session);
        bool changed = next.Signature != _roster?.Signature;
        _roster = next;
        _rosterButton.Enabled = true;
        _rosterButton.Visible = true;
        _tips.SetToolTip(_rosterButton, "查看我方阵容与交换池的英雄梯队");
        _rosterButton.Text = $"可选英雄 · {next.Team.Count(t => t.ChampionId > 0) + next.Bench.Length}";
        if (changed)
        {
            _rosterChanged?.Invoke();
        }
    }

    private void ClearRoster()
    {
        _roster = null;
        // Only ARAM-style queues have a bench; elsewhere the button would only be a disabled placeholder.
        _rosterButton.Enabled = false;
        _rosterButton.Visible = false;
        _rosterButton.Text = "可选英雄";
        _tips.SetToolTip(_rosterButton, "等待客户端提供大乱斗选人数据");
        _rosterWindow?.Close();
    }

    private void ShowRoster()
    {
        if (_roster == null)
        {
            return;
        }
        if (_rosterWindow is { IsDisposed: false })
        {
            _rosterWindow.Activate();
            return;
        }
        int queue = _queue;
        bool mayhem = queue == 2400 || SelectedMode == "mayhem";
        var query = (SelectedQuery with { Mode = "aram", ChampionId = 0 }).Normalize();
        var window = new Controls.AssistantRosterWindow(
            this,
            queue,
            mayhem,
            _tips,
            () => _roster,
            () => _queue,
            () => _owner.GetCurrentPuuid(),
            () =>
                TimeSpan.FromMinutes(Math.Clamp(_owner.GetAppConfig()?.ChampionAnalysisCacheMinutes ?? 1440, 1, 10080)),
            token => LoadRosterRanksAsync(mayhem, query, token),
            async (message, token) =>
            {
                var client =
                    FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接。");
                return await AnnounceRunesAsync(client, message, token);
            },
            id =>
                _owner.OpenAssistantGuide(
                    id,
                    mayhem ? 2400 : 0,
                    "none",
                    mayhem ? null : query with { ChampionId = id }
                ),
            _masteryService
        );
        _rosterWindow = window;
        _rosterChanged = window.UpdateRoster;
        window.WorkRetired += work => TrackAssistantWork(work);
        window.Shown += (_, _) => PositionEquipmentEditor(window, Dip(340, 360));
        window.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_rosterWindow, window))
            {
                _rosterChanged = null;
                _rosterWindow = null;
            }
            window.Dispose();
        };
        window.Show(this);
    }

    private async Task<(Dictionary<int, ChampionRosterTier> Ranks, string Version)> LoadRosterRanksAsync(
        bool mayhem,
        OpggQuery query,
        CancellationToken token
    )
    {
        var ranks = new Dictionary<int, ChampionRosterTier>();
        if (mayhem)
        {
            var page = await _am.GetAsync(0, false, token);
            foreach (var row in page.Index.Champions)
            {
                ranks[row.Id] = new(row.Tier, row.WinRate, page.Index.PickRate(row));
            }
            return (ranks, page.Index.Patch + (page.UpdateFailed ? " · 旧缓存" : ""));
        }
        var tiers = await _opgg.GetTiersAsync(query, false, token);
        foreach (var row in tiers.Rows)
        {
            ranks[row.ChampionId] = new(row.Tier.HasValue ? "T" + row.Tier : "—", row.WinRate, row.PickRate);
        }
        return (ranks, tiers.Version);
    }
}
