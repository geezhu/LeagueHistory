namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private async Task ReadVoiceCommand()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _gameBar.PollVoiceAsync(
                UseGameBar
                    && (_owner.GetAppConfig()?.EnableLoadingOverlay ?? true)
                    && _phase == "InProgress"
                    && Environment.TickCount64 - _lastPhaseRead < 15000
                    && !_sendingChat,
                _life.Token
            );
        }
        catch (OperationCanceledException) { }
    }
}
