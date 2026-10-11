using League.Controls;

namespace League;

public partial class FormMain
{
    private void ConfigureVoiceSettings(FlowLayoutPanel parent)
    {
        var controller = new VoiceSettingsController(
            () => _appConfig?.VoiceMicrophoneId ?? "",
            id =>
            {
                if (_appConfig != null)
                {
                    _appConfig.VoiceMicrophoneId = id;
                    SaveAppConfig();
                }
            }
        );
        SettingsCard(parent, "本机语音识别", controller.Content);
        controller.Attach();
    }
}
