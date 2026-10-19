using System.Media;

namespace League.Services;

/// <summary>
/// Chimes when a watched champion is on the enemy team as a game starts. The client fills the roster in over several
/// reads; each watched champion sounds once per game, so a later, fuller read chimes only for champions it adds.
/// Played by the main program, like the other alerts, because Windows mutes the Game Bar widget in game.
/// </summary>
public sealed class ChampionAlerts : IDisposable
{
    // The client lists each champion again at id + 60000 for its classic-mode variant: Jade_Shaco is 60035.
    private const int VariantOffset = 60000;

    private readonly Action _play;
    private readonly HashSet<int> _announced = new();
    private readonly object _gate = new();
    private long? _generation;
    private SoundPlayer? _player;

    public ChampionAlerts(Action? play = null) => _play = play ?? PlaySystemSound;

    /// <summary>The watched enemies not yet announced this game; one chime plays when there are any.</summary>
    public IReadOnlyList<int> Update(long generation, IEnumerable<int> enemies, IEnumerable<int> watched)
    {
        var wanted = watched.Select(BaseId).ToHashSet();
        var found = new List<int>();
        lock (_gate)
        {
            if (_generation != generation)
            {
                _generation = generation;
                _announced.Clear();
            }

            foreach (int id in enemies.Select(BaseId))
            {
                if (id > 0 && wanted.Contains(id) && _announced.Add(id))
                {
                    found.Add(id);
                }
            }
        }

        if (found.Count > 0)
        {
            _play();
        }

        return found;
    }

    /// <summary>The champion a classic-mode variant stands for, so a watched champion also matches its variant.</summary>
    public static int BaseId(int id) => id > VariantOffset ? id - VariantOffset : id;

    /// <summary>Plays the chime once, for the settings page's preview.</summary>
    public void Play() => _play();

    // SoundPlayer.Play returns at once and plays on its own thread from the loaded buffer, so the player is kept
    // until the next chime replaces it.
    private void PlaySystemSound()
    {
        lock (_gate)
        {
            _player?.Stop();
            _player?.Stream?.Dispose();
            _player?.Dispose();
            _player = new SoundPlayer(new MemoryStream(RespawnMarkSounds.ChampionAlertWave.Value));
            try
            {
                _player.Play();
            }
            catch (InvalidOperationException e)
            {
                // No audio device: the loading screen still shows the enemy champions.
                AppLog.Shared.Warn(LogCategory.Game, "英雄提醒：提示音无法播放", e);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _player?.Stop();
            _player?.Stream?.Dispose();
            _player?.Dispose();
            _player = null;
        }
    }
}
