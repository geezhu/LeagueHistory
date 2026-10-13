using League.Controls;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

public sealed class PlayerCardUIManager
{
    private readonly Form _form;
    private readonly TableLayoutPanel _table;
    private readonly PlayerChampionTierService? _championTiers;
    private readonly Dictionary<string, (BorderPanel Panel, PlayerCardControl Card, long Revision)> _cards = new();
    private readonly CancellationTokenSource _closed = new();
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action<string, long>? RetryRequested { get; set; }

    public PlayerCardUIManager(FormMain form) : this(form, form.tableLayoutPanel1)
    {
        _championTiers = new(form);
        form.Disposed += (_, _) => _championTiers.Dispose();
    }

    // Also accepts an ordinary Form for isolated WinForms regression tests.
    public PlayerCardUIManager(Form form, TableLayoutPanel table)
    {
        _form = form;
        _table = table;
        PlayerTeamsLayout.Attach(table);
        if (form.IsHandleCreated) _ready.TrySetResult();
        form.HandleCreated += (_, _) => _ready.TrySetResult();
        form.Disposed += (_, _) => _closed.Cancel();
    }

    public Task DispatchAsync(Action action)
    {
        if (_form.IsDisposed || _closed.IsCancellationRequested) return Task.CompletedTask;
        if (Environment.CurrentManagedThreadId == _uiThread)
        {
            action();
            return Task.CompletedTask;
        }
        if (!_form.IsHandleCreated) return DispatchWhenReadyAsync(action);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _form.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!_form.IsDisposed) action();
                    completion.TrySetResult();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            }));
        }
        catch (InvalidOperationException) { return Task.CompletedTask; }
        return completion.Task.WaitAsync(_closed.Token);
    }

    private async Task DispatchWhenReadyAsync(Action action)
    {
        await _ready.Task.WaitAsync(_closed.Token);
        await DispatchAsync(action);
    }

    public void Render(IReadOnlyList<RoomPlayerState> states)
    {
        _table.SuspendLayout();
        try
        {
            var wanted = states.Select(s => s.Key).ToHashSet();
            foreach (var pair in _cards.Where(p => !wanted.Contains(p.Key) || p.Value.Card.IsDisposed).ToArray())
            {
                pair.Value.Panel.Dispose();
                _cards.Remove(pair.Key);
            }
            var owned = _cards.Values.Select(c => (Control)c.Panel).ToHashSet();
            foreach (Control control in _table.Controls.Cast<Control>().ToArray())
                if (!owned.Contains(control)) control.Dispose();

            // Detach moved cards first so swapping slots never overflows the fixed 5x2 grid.
            foreach (var state in states)
                if (_cards.TryGetValue(state.Key, out var item) && item.Panel.Parent == _table
                    && (_table.GetRow(item.Panel) != (state.Ally ? 0 : 1) || _table.GetColumn(item.Panel) != state.Column))
                    _table.Controls.Remove(item.Panel);

            foreach (var state in states)
            {
                bool created = !_cards.TryGetValue(state.Key, out var item);
                if (created)
                {
                    var panel = new BorderPanel { Tag = state.Ally, BorderColor = Color.FromArgb(221,230,240), BorderWidth = 1,
                        Padding = new Padding(2), Dock = DockStyle.Fill, Margin = new Padding(5) };
                    var card = new PlayerCardControl { Dock = DockStyle.Fill, Margin = Padding.Empty };
                    panel.Controls.Add(card);
                    item = (panel, card, -1);
                }
                if (item.Revision != state.Revision)
                {
                    ApplyState(item.Card, state);
                    item.Revision = state.Revision;
                }
                if (item.Panel.Parent != _table) _table.Controls.Add(item.Panel, state.Column, state.Ally ? 0 : 1);
                _cards[state.Key] = item;
                if (state.Info?.Player is { } player)
                {
                    item.Card.lblPlayerName.LinkColor = player.NameColor;
                    item.Card.lblPlayerName.VisitedLinkColor = player.NameColor;
                    item.Card.lblPlayerName.ActiveLinkColor = player.NameColor;
                }
                if (created && _form is FormMain main && _championTiers != null)
                {
                    var card = item.Card;
                    card.BindChampionInfo(id => _championTiers.GetAsync(id, card.CurrentPuuId),
                        (id, tier) => main.OpenAssistantGuide(id, tier.Queue, tier.Position));
                    _ = card.LoadSummonerProfileAsync();
                }
            }
        }
        finally { _table.ResumeLayout(true); }
    }

    private void ApplyState(PlayerCardControl card, RoomPlayerState state)
    {
        long sid = state.Player.Value<long?>("summonerId") ?? 0;
        int champion = state.Player.Value<int?>("championId") ?? 0;
        string puuid = state.Player.Value<string>("puuid") ?? "";
        var player = state.Info?.Player;
        string name = player?.GameName ?? (state.CanRetry ? "战绩读取失败" : "加载中…");
        card.SetPlayerInfo(name, player?.SoloRank ?? "", player?.FlexRank ?? "", player?.Avatar!, player?.IsPublic ?? "",
            state.Info?.MatchItems ?? new(), player?.NameColor ?? Color.FromArgb(36,63,98), sid, champion, puuid);
        if (state.Info != null)
        {
            card.SetRecentStatistics(state.Info);
            card.ListViewControl.SmallImageList = state.Info.HeroIcons;
        }
        else
        {
            card.ClearRecentStatistics();
            card.ListViewControl.SmallImageList = null;
        }
        string status = state.Status;
        if (state.Info != null && state.Info.MatchItems.Count == 0 && status.Length == 0)
            status = state.Player.Value<bool?>("__roomSelectionOnly") == true ? "客户端未提供玩家资料，战绩不可用"
                : player?.GameName?.Contains("隐藏") == true ? "该玩家未公开战绩" : "当前模式暂无战绩";
        card.SetHistoryLoadStatus(status, state.Info == null && !state.CanRetry, state.CanRetry,
            () => RetryRequested?.Invoke(state.Key, state.Generation));
    }
}
