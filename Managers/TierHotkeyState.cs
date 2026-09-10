namespace League.Managers;

/// <summary>Detects a chord's rising edge without repeating while keys remain held.</summary>
internal sealed class TierHotkeyState
{
    private bool _myHeld;
    private bool _enemyHeld;

    public Keys[] Read(bool control, bool f9, bool f11)
    {
        bool my = control && f9, enemy = control && f11;
        var pressed = new List<Keys>(2);
        if (my && !_myHeld) pressed.Add(Keys.F9);
        if (enemy && !_enemyHeld) pressed.Add(Keys.F11);
        _myHeld = my;
        _enemyHeld = enemy;
        return pressed.ToArray();
    }
}
