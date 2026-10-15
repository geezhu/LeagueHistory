using System.Media;

namespace League.Services;

public enum RespawnTone
{
    Soon,
    Revive,
}

/// <summary>A marked champion's state on one 2999 poll.</summary>
public sealed record RespawnMarkState(string Key, bool Dead, double Seconds);

/// <summary>
/// Chimes for marked champions, played by the main program: the Game Bar widget is a background UWP app while
/// the game has focus, where Windows mutes its audio. Five seconds left is one tick and respawn rises; a death
/// itself stays silent, since the text reminder already shows it and a chime then is noise mid-fight.
/// </summary>
public sealed class RespawnMarkSounds : IDisposable
{
    public const double SoonSeconds = 5;

    private readonly Action<RespawnTone> _play;
    private readonly Dictionary<string, (bool Dead, bool Soon)> _states = new(StringComparer.Ordinal);
    private long _test = -1;

    public RespawnMarkSounds(Action<RespawnTone>? play = null) => _play = play ?? PlaySystemSound;

    /// <summary>Plays at most one tone per champion per poll; unmarked champions are forgotten.</summary>
    public void Update(IReadOnlyList<RespawnMarkState> marked, bool sound, long test)
    {
        if (sound && _test >= 0 && test != _test)
        {
            _play(RespawnTone.Soon);
        }
        _test = test;

        var keys = marked.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _states.Keys.Where(k => !keys.Contains(k)).ToArray())
        {
            _states.Remove(key);
        }

        foreach (var mark in marked)
        {
            bool soon = mark.Dead && mark.Seconds <= SoonSeconds;
            if (!_states.TryGetValue(mark.Key, out var before))
            {
                // A newly marked champion starts silent; only later changes are announced.
                _states[mark.Key] = (mark.Dead, soon);
                continue;
            }

            // A death with five seconds or less left reaches the warning at once.
            RespawnTone? tone =
                soon && !before.Soon ? RespawnTone.Soon
                : !mark.Dead && before.Dead ? RespawnTone.Revive
                : null;
            _states[mark.Key] = (mark.Dead, soon || (mark.Dead && before.Soon));
            if (sound && tone is { } chime)
            {
                _play(chime);
            }
        }
    }

    public void Reset()
    {
        _states.Clear();
        _test = -1;
    }

    private static readonly Lazy<byte[]> SoonWave = new(() => Tone(988));
    private static readonly Lazy<byte[]> ReviveWave = new(() => Tone(660, 990));

    /// <summary>A summoner spell coming back: three rising notes, apart from the respawn chimes.</summary>
    public static readonly Lazy<byte[]> SummonerReadyWave = new(() => Tone(784, 988, 1175));
    private SoundPlayer? _player;

    // SoundPlayer.Play returns at once and plays on its own thread; a new chime replaces one still playing.
    private void PlaySystemSound(RespawnTone tone)
    {
        byte[] wave = tone == RespawnTone.Soon ? SoonWave.Value : ReviveWave.Value;
        _player?.Stop();
        _player?.Stream?.Dispose();
        _player?.Dispose();
        _player = new SoundPlayer(new MemoryStream(wave));
        try
        {
            _player.Play();
        }
        catch (InvalidOperationException e)
        {
            // No audio device: the Game Bar text reminder still shows.
            ChatSendDiagnostics.Shared.Report("respawn-sound", "提示音无法播放：" + e.Message);
        }
    }

    /// <summary>A 16-bit mono WAV: each note 130 ms with a soft attack and release, 40 ms apart.</summary>
    public static byte[] Tone(params double[] notes)
    {
        const int rate = 22050,
            note = rate * 130 / 1000,
            gap = rate * 40 / 1000;
        var samples = new List<short>();
        foreach (double frequency in notes)
        {
            for (int i = 0; i < note; i++)
            {
                double envelope = Math.Min(1, Math.Min(i / (rate * 0.01), (note - i) / (rate * 0.04)));
                samples.Add((short)(9000 * envelope * Math.Sin(2 * Math.PI * frequency * i / rate)));
            }
            samples.AddRange(Enumerable.Repeat((short)0, gap));
        }

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        int data = samples.Count * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + data);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }
        writer.Flush();
        return buffer.ToArray();
    }

    public void Dispose()
    {
        _player?.Stop();
        _player?.Stream?.Dispose();
        _player?.Dispose();
        _player = null;
    }
}
