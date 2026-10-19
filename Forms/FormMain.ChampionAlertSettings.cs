using League.Controls;
using League.Services;

namespace League;

public partial class FormMain
{
    private void ConfigureChampionAlertSettings(FlowLayoutPanel parent)
    {
        var preview = new ChampionAlerts();
        Disposed += (_, _) => preview.Dispose();
        var controller = new ChampionAlertSettingsController(
            () => Globals.resLoading?.Champions?.Champions.ToArray() ?? [],
            () => _appConfig?.AlertChampionIds ?? [],
            () => OpenChampionAlertPicker(preview),
            preview.Play
        );
        SettingsCard(parent, "英雄提醒", controller.Content);
        controller.Attach();
    }

    private void OpenChampionAlertPicker(ChampionAlerts preview)
    {
        if (_uiManager?.LcuReady != true || _appConfig == null)
        {
            MessageBox.Show(
                "请先登录英雄联盟客户端并等待连接成功！",
                "客户端未连接",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        using var picker = new ChampionAlertPicker(
            _appConfig.AlertChampionIds,
            ids =>
            {
                // Replaced, not changed in place: the game-start check reads the list from another thread.
                _appConfig.AlertChampionIds = ids.ToList();
                SaveAppConfig();
            },
            preview.Play
        );
        picker.ShowDialog(this);
    }
}
