using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private string _masteryPuuid = "";
    private readonly ChampionMasteryService _masteryService=ChampionMasteryService.Shared;
    private Label? _masteryLabel;
    private Panel? _masteryRow;
    private Button? _sendMastery;
    private PlayerChampionMastery? _mastery;
    private bool _sendingMastery;
    private bool _masteryBusy;
    private long _masteryNextCheck;
    private void AddMasterySummary()
    {
        if (_hero <= 0) return;
        _mastery = null;
        _masteryRow = new Panel { Size = new Size(282, 22), Margin = Padding.Empty };
        _masteryLabel = new Label { Text = "成就读取中…", Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(164, 188, 199) };
        _sendMastery = new Button { Text = "发送熟练度", Dock = DockStyle.Right, Width = 84, Enabled = false,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(16, 45, 57), ForeColor = Color.FromArgb(208, 185, 130), Padding = Padding.Empty };
        _sendMastery.FlatAppearance.BorderColor = Color.FromArgb(49, 83, 95);
        _sendMastery.Click += async (_, _) => await SendMasteryAsync();
        _tips.SetToolTip(_sendMastery, "将自己当前英雄的成就等级和点数发送到选人聊天；缓存数据会标注。英雄成就不区分游戏模式。");
        _masteryRow.Controls.Add(_masteryLabel); _masteryRow.Controls.Add(_sendMastery);
        _body.Controls.Add(_masteryRow); _masteryNextCheck = 0;
        _ = RefreshMastery();
    }
    private async Task RefreshMastery()
    {
        var label = _masteryLabel;
        if (_masteryBusy || label == null || label.IsDisposed || Environment.TickCount64 < _masteryNextCheck || _hero <= 0) return;
        int hero = _hero; string puuid = _masteryPuuid; var token = _load?.Token ?? default;
        _masteryBusy = true;
        try
        {
            var page = await _masteryService.GetAsync(puuid, token);
            if (IsDisposed || label.IsDisposed || token.IsCancellationRequested || hero != _hero || puuid != _masteryPuuid) return;
            var mastery = page.ForChampion(hero);
            _mastery = mastery;
            label.Text = mastery.Text; _tips.SetToolTip(label, mastery.Description);
            if (_sendMastery is { IsDisposed: false }) _sendMastery.Enabled = mastery.Available && _selecting && !_sendingMastery;
            _masteryNextCheck = Environment.TickCount64 + (mastery.Stale ? 3000 : 15000);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!label.IsDisposed && hero == _hero && puuid == _masteryPuuid) { label.Text = "成就暂不可用"; _masteryNextCheck = Environment.TickCount64 + 30000; } }
        finally { _masteryBusy = false; }
    }

    private async Task SendMasteryAsync(HttpClient? client = null)
    {
        var mastery = _mastery; var button = _sendMastery;
        if (_sendingMastery || !_selecting || mastery is not { Available: true } || mastery.ChampionId != _hero) return;
        int hero = _hero; string puuid = _masteryPuuid;
        bool Current() => !IsDisposed && _selecting && _hero == hero && _masteryPuuid == puuid && button is { IsDisposed: false } && button == _sendMastery;
        _sendingMastery = true; button!.Enabled = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_load?.Token ?? default);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            client ??= FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接。");
            var session = JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session", timeout.Token));
            var own = (session["myTeam"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (int?)p["cellId"] == (int?)session["localPlayerCellId"]);
            int selected = (int?)own?["championId"] ?? 0;
            if (selected <= 0) selected = (session["actions"] as JArray ?? new()).OfType<JArray>().SelectMany(a => a.OfType<JObject>())
                .Where(a => (int?)a["actorCellId"] == (int?)session["localPlayerCellId"] && (string?)a["type"] == "pick")
                .Select(a => (int?)a["championId"] ?? 0).LastOrDefault(id => id > 0);
            string identity = (string?)own?["puuid"] ?? "";
            if (!Current() || own == null || selected != hero || (!string.IsNullOrWhiteSpace(identity) && identity != Guid.Empty.ToString() && identity != puuid))
            { if (Current()) _status.Text = "未发送：所选英雄已变化，请等待更新。"; return; }
            string name = FormMain.Globals.resLoading?.GetChampionById(hero)?.Name ?? $"英雄 {hero}";
            var messages = ChampionMasteryChatMessage.Build(OverlayChampionDetailService.ModeName(PlayerChampionTier.NormalizeQueue(_queue, "")),
                "自己", [new(name, mastery)]);
            bool sent = await AnnounceRunesAsync(client, messages[0], timeout.Token, Current, deduplicate: false);
            if (Current()) _status.Text = sent ? "熟练度已发送到选人聊天。" : "未发送：请确认仍在选人阶段且聊天已连接。";
        }
        catch (OperationCanceledException) { if (Current()) _status.Text = "熟练度发送超时，请重试。"; }
        catch (Exception) { if (Current()) _status.Text = "熟练度发送失败，请检查客户端连接。"; }
        finally
        {
            _sendingMastery = false;
            if (_sendMastery is { IsDisposed: false }) _sendMastery.Enabled = _selecting && _mastery?.Available == true && _mastery.ChampionId == _hero;
        }
    }
}
