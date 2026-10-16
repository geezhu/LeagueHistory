using System.Media;
using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // Cannon waves sound from the main program, like the other alerts, because Windows mutes the widget in game.
    private readonly CannonWaveAlerts _cannonAlerts = new();
    private SoundPlayer? _cannonPlayer;

    private void AnnounceCannonWave(MinionWaveForecast? waves, double gameTime)
    {
        string mode = UseGameBar ? _controlSettings.CannonSound : CannonSoundModes.Off;
        if (!_cannonAlerts.Update(waves?.Spawns, gameTime, mode))
        {
            return;
        }

        ChatSendDiagnostics.Shared.Report(
            "cannon-wave",
            (mode == CannonSoundModes.Next ? "下一波是炮车波" : "炮车波出兵")
                + " "
                + MinionWaveForecaster.Clock(gameTime)
        );
        _cannonPlayer?.Dispose();
        _cannonPlayer = new SoundPlayer(new MemoryStream(RespawnMarkSounds.CannonWave.Value));
        try
        {
            _cannonPlayer.Play();
        }
        catch (InvalidOperationException e)
        {
            // No audio device: the highlighted wave button still shows the cannon wave.
            ChatSendDiagnostics.Shared.Report("cannon-wave", "提示音无法播放：" + e.Message);
        }
    }
}
