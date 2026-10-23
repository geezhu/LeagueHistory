namespace League.Controls;

/// <summary>
/// A read-only box that records the next key combination pressed in it. Backspace or Delete asks for the default.
/// The shortcuts registered at the time are taken by Windows before they reach any window, so they never arrive here.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;
        Width = 120;
    }

    /// <summary>A key combination pressed in the box, modifiers included; it may not be usable as a shortcut.</summary>
    public event Action<Keys>? Chosen;

    public event Action? ResetRequested;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            ResetRequested?.Invoke();
            return;
        }

        // A modifier on its own is still being held towards a combination.
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return;
        }

        Chosen?.Invoke(e.KeyData);
    }
}
