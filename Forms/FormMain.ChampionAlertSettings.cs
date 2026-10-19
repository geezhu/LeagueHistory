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
            ids =>
            {
                if (_appConfig != null)
                {
                    // Replaced, not changed in place: the game-start check reads the list from another thread.
                    _appConfig.AlertChampionIds = ids.ToList();
                    SaveAppConfig();
                }
            },
            preview.Play
        );
        SettingsCard(parent, "英雄提醒", controller.Content);
        controller.Attach();
    }
}
