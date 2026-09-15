using System.Runtime.InteropServices;

namespace League.Managers;

/// <summary>Pauses desktop overlays when Windows reports exclusive Direct3D fullscreen.</summary>
internal sealed class OverlayFullscreenGuard
{
    private readonly Func<int?> _query;
    private long _nextCheck;
    private bool _exclusive;

    public OverlayFullscreenGuard(Func<int?>? query = null) => _query = query ?? QueryState;

    public bool ShouldPause(long now, bool force = false)
    {
        if (!force && now < _nextCheck) return _exclusive;
        _nextCheck = now + 1000;
        // QUNS_BUSY also covers borderless windows and presentations; only value 3 is exclusive D3D.
        _exclusive = _query() == 3;
        return _exclusive;
    }

    public void Reset() { _nextCheck = 0; _exclusive = false; }

    private static int? QueryState() => SHQueryUserNotificationState(out int state) == 0 ? state : null;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
