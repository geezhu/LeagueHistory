namespace League.Models;

/// <summary>Visibility intent is independent of statistics readiness and temporary loss of game focus.</summary>
public sealed class LoadingOverlayDisplayState
{
    public bool Requested { get; private set; }
    public bool Preview { get; private set; }
    public bool Suppressed { get; private set; }
    private bool _active;

    public void ObservePhase(string phase)
    {
        bool active = LoadingRoster.CanDisplay(phase);
        if (!active)
        {
            _active = false;
            Suppressed = false;
            if (!Preview)
            {
                Requested = false;
            }

            return;
        }
        if (!_active)
        {
            Suppressed = false;
        }

        _active = true;
        if (!Suppressed)
        {
            Requested = true;
            Preview = false;
        }
    }

    public void Request()
    {
        Requested = true;
        Preview = false;
        Suppressed = false;
    }

    public void RequestPreview()
    {
        Requested = true;
        Preview = true;
    }

    public void Dismiss()
    {
        Requested = false;
        Preview = false;
        Suppressed = true;
    }

    public void Reset()
    {
        Requested = false;
        Preview = false;
        Suppressed = false;
        _active = false;
    }
}
