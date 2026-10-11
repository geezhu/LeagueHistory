using League.Controls;
using League.Services;

namespace League;

public partial class FormMain
{
    private void ConfigureShutdownSettings(FlowLayoutPanel parent, ScheduledShutdownService? schedulerService = null)
    {
        var controller = new ShutdownSettingsController(
            this,
            tabControl1.Font,
            schedulerService ?? new ScheduledShutdownService()
        );
        SettingsCard(parent, "定时关机 · 每日计划", controller.Content);
        controller.Attach();
    }
}
