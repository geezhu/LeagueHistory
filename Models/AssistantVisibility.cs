namespace League.Models;

/// <summary>Tolerate transient foreground/handle changes while keeping explicit exits immediate.</summary>
public sealed class AssistantVisibility
{
    private long? _missingSince;
    public bool ShouldShow(bool selecting,bool minimized,bool available,bool focused,bool dragging,long now)
    {
        if(!selecting || minimized){_missingSince=null;return false;}
        if(dragging || available && focused){_missingSince=null;return true;}
        _missingSince??=now;
        return now-_missingSince.Value<1500;
    }
}
