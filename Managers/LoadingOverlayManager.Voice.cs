namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // In a Game Bar game a start request opens the microphone within 100 ms and readiness is published as fast:
    // speech before the first audio packet is never captured. Elsewhere the slower poll only clears stale files.
    private const int ActiveVoicePollMilliseconds = 100,
        IdleVoicePollMilliseconds = 400;
    private readonly System.Windows.Forms.Timer _voicePoll = new() { Interval = IdleVoicePollMilliseconds };

    private async Task ReadVoiceCommand()
    {
        if (_disposed)
        {
            return;
        }

        bool enabled =
            UseGameBar
            && (_owner.GetAppConfig()?.EnableLoadingOverlay ?? true)
            && _phase == "InProgress"
            && Environment.TickCount64 - _lastPhaseRead < 15000
            && !_sendingChat;
        _voicePoll.Interval = enabled ? ActiveVoicePollMilliseconds : IdleVoicePollMilliseconds;
        try
        {
            await _gameBar.PollVoiceAsync(enabled, _life.Token);
        }
        catch (OperationCanceledException)
        {
            // Stopping the manager ends polling without publishing another recording state.
        }
    }
}
