namespace League.Models;

/// <summary>Tolerate transient client handle loss while keeping explicit exits immediate.</summary>
/// <remarks>
/// Foreground focus is not required: the panel is an owned window stacked directly above the client,
/// so a background client keeps its panel beneath whatever application covers it.
/// </remarks>
public sealed class AssistantVisibility
{
    private long? _missingSince;

    public bool ShouldShow(bool selecting, bool minimized, bool available, bool dragging, long now)
    {
        if (!selecting || minimized)
        {
            _missingSince = null;
            return false;
        }
        if (dragging || available)
        {
            _missingSince = null;
            return true;
        }
        _missingSince ??= now;
        return now - _missingSince.Value < 1500;
    }
}
