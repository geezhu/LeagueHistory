namespace League.Models;

public enum GameHotkeyAction
{
    ChampSelect,
    MyTeam,
    FullTeam,
}

/// <summary>
/// The three chat shortcuts as the settings store them ("Ctrl+F7", "F9", "F11", upstream 3d379ba's spelling) and as
/// Windows registers them. They are registered system-wide, so a shortcut needs a function key or Ctrl/Alt: a bare
/// letter would stop that letter being typed anywhere while the app runs.
/// </summary>
public static class GameHotkeys
{
    public static readonly IReadOnlyDictionary<GameHotkeyAction, Keys> Defaults = new Dictionary<GameHotkeyAction, Keys>
    {
        [GameHotkeyAction.ChampSelect] = Keys.Control | Keys.F7,
        [GameHotkeyAction.MyTeam] = Keys.F9,
        [GameHotkeyAction.FullTeam] = Keys.F11,
    };

    /// <summary>The configured shortcut of each action; a missing or unusable setting falls back to the default.</summary>
    public static IReadOnlyDictionary<GameHotkeyAction, Keys> From(LeagueConfig? config) =>
        new Dictionary<GameHotkeyAction, Keys>
        {
            [GameHotkeyAction.ChampSelect] = Parse(config?.HotkeyChampSelect, Defaults[GameHotkeyAction.ChampSelect]),
            [GameHotkeyAction.MyTeam] = Parse(config?.HotkeyMyTeam, Defaults[GameHotkeyAction.MyTeam]),
            [GameHotkeyAction.FullTeam] = Parse(config?.HotkeyFullTeam, Defaults[GameHotkeyAction.FullTeam]),
        };

    public static string Setting(LeagueConfig config, GameHotkeyAction action) =>
        action switch
        {
            GameHotkeyAction.ChampSelect => config.HotkeyChampSelect,
            GameHotkeyAction.MyTeam => config.HotkeyMyTeam,
            _ => config.HotkeyFullTeam,
        };

    public static void Store(LeagueConfig config, GameHotkeyAction action, Keys keys)
    {
        string text = Format(keys);
        switch (action)
        {
            case GameHotkeyAction.ChampSelect:
                config.HotkeyChampSelect = text;
                break;
            case GameHotkeyAction.MyTeam:
                config.HotkeyMyTeam = text;
                break;
            default:
                config.HotkeyFullTeam = text;
                break;
        }
    }

    /// <summary>"Ctrl+Alt+Shift+F7" and its parts in any order and case; anything unusable gives the fallback.</summary>
    public static Keys Parse(string? text, Keys fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        Keys keys = Keys.None;
        foreach (string part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Keys? piece = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Keys.Control,
                "ALT" => Keys.Alt,
                "SHIFT" => Keys.Shift,
                _ when Enum.TryParse(part, ignoreCase: true, out Keys key) && !IsModifierKey(key) => key,
                _ => null,
            };
            if (piece == null)
            {
                return fallback;
            }

            keys |= piece.Value;
        }

        return IsAllowed(keys) ? keys : fallback;
    }

    public static string Format(Keys keys)
    {
        var parts = new List<string>();
        if ((keys & Keys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((keys & Keys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((keys & Keys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add((keys & Keys.KeyCode).ToString());
        return string.Join("+", parts);
    }

    /// <summary>A key besides the modifiers, and either a function key or Ctrl/Alt held with it.</summary>
    public static bool IsAllowed(Keys keys)
    {
        Keys code = keys & Keys.KeyCode;
        if (code == Keys.None || IsModifierKey(code))
        {
            return false;
        }

        return code is >= Keys.F1 and <= Keys.F24 || (keys & (Keys.Control | Keys.Alt)) != 0;
    }

    private static bool IsModifierKey(Keys code) =>
        code
            is Keys.ControlKey
                or Keys.LControlKey
                or Keys.RControlKey
                or Keys.ShiftKey
                or Keys.LShiftKey
                or Keys.RShiftKey
                or Keys.Menu
                or Keys.LMenu
                or Keys.RMenu
                or Keys.LWin
                or Keys.RWin;
}
