using League.States;
using Newtonsoft.Json.Linq;

namespace League;

public partial class FormMain
{
    private bool _openingPlayerProfile;

    // Shared by history player icons and detail player names.
    public async Task OpenPlayerProfileAsync(string summonerId)
    {
        if (_openingPlayerProfile || string.IsNullOrWhiteSpace(summonerId) || summonerId == "0")
            return;
        _openingPlayerProfile = true;
        _uiManager!.ShowLoadingIndicator();
        try
        {
            JObject? summoner = await Globals.lcuClient.GetGameNameBySummonerId(summonerId);
            if (summoner != null && !string.IsNullOrWhiteSpace(summoner["puuid"]?.ToString()))
            {
                RefreshState.ForceMatchRefresh = true;
                var rankedStats = await GetRankedStatsAsync(summoner["puuid"]?.ToString() ?? "");
                string privacyStatus = GetPrivacyStatus(summoner);

                _matchTabContent?.CreateNewTab(
                    summoner["summonerId"]?.ToString() ?? "",
                    summoner["gameName"]?.ToString() ?? "",
                    summoner["tagLine"]?.ToString() ?? "",
                    summoner["puuid"]?.ToString() ?? "",
                    summoner["profileIconId"]?.ToString() ?? "",
                    summoner["summonerLevel"]?.ToString() ?? "",
                    privacyStatus,
                    rankedStats
                );
                txtGameName.Text = "";
                imageTabControl1.SelectedIndex = 0;
                Activate();
            }
            else
                MessageBox.Show("无法获取该玩家的资料，请确认客户端已连接后重试。");
        }
        catch (Exception ex)
        {
            MessageBox.Show("查询失败: " + ex.Message);
        }
        finally
        {
            _openingPlayerProfile = false;
            _uiManager!.HideLoadingIndicator();
        }
    }
}
