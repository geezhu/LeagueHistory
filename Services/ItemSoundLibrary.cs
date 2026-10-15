using NVorbis;

namespace League.Services;

/// <summary>
/// Survival items' own sound effects, downloaded once from the official League of Legends Wiki (which hosts the
/// game's item sounds as Ogg Vorbis) and kept as WAV, the format the chimes already play. Items the Wiki has no
/// sound for borrow the one of an item with the same effect.
/// </summary>
public sealed class ItemSoundLibrary
{
    // The Wiki refuses requests that pretend to be a browser and serves files to a client that names itself.
    private const string UserAgent = "LeagueHistory/1.0 (https://github.com/geezhu/LeagueHistory)";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The Wiki file played for each base item id: the activation or the passive's trigger.</summary>
    public static readonly IReadOnlyDictionary<int, string> Files = new Dictionary<int, string>
    {
        [3157] = "Zhonya's_Hourglass_active_SFX.ogg",
        // Seeker's Armguard has the same stasis as Zhonya's Hourglass.
        [2420] = "Zhonya's_Hourglass_active_SFX.ogg",
        [3026] = "Guardian_Angel_passive_SFX.ogg",
        [3140] = "Quicksilver_Sash_active_SFX.ogg",
        // Mercurial Scimitar keeps Quicksilver Sash's cleanse.
        [3139] = "Quicksilver_Sash_active_SFX.ogg",
        [3102] = "Banshee's_Veil_passive_SFX.ogg",
        // Verdant Barrier's spell shield is Banshee's Veil's.
        [4632] = "Banshee's_Veil_passive_SFX.ogg",
        [3814] = "Edge_of_Night_passive_SFX.ogg",
        [3053] = "Sterak's_Gage_passive_SFX.ogg",
        // The Wiki's file name has the typo, not this table.
        [6673] = "Immortal_Shieldbox_passive_SFX.ogg",
        [3155] = "Hexdrinker_and_Maw_of_Malmortius_passive_SFX.ogg",
        [3156] = "Hexdrinker_and_Maw_of_Malmortius_passive_SFX.ogg",
        [3193] = "Gargoyle_Stoneplate_active_SFX.ogg",
    };

    private readonly string _folder;

    public ItemSoundLibrary(string? folder = null) =>
        _folder =
            folder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "item-sounds"
            );

    /// <summary>The item's sound as a WAV file in memory; null for an item without one.</summary>
    public async Task<byte[]?> LoadAsync(int itemId, CancellationToken token)
    {
        if (!Files.TryGetValue(SurvivalItems.BaseId(itemId), out string? file))
        {
            return null;
        }

        string path = Path.Combine(_folder, Path.ChangeExtension(file, ".wav"));
        if (File.Exists(path))
        {
            return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        }

        string url = "https://wiki.leagueoflegends.com/en-us/Special:FilePath/" + Uri.EscapeDataString(file);
        byte[] ogg = await Http.GetByteArrayAsync(url, token).ConfigureAwait(false);
        byte[] wave = ToWave(ogg);
        Directory.CreateDirectory(_folder);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, wave, token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
        return wave;
    }

    /// <summary>Decodes Ogg Vorbis into 16-bit PCM WAV with the stream's own rate and channels.</summary>
    public static byte[] ToWave(byte[] ogg)
    {
        // The Wiki can answer with a "please wait" page instead of the file; that must not be cached as a sound.
        if (ogg.Length < 4 || !ogg.AsSpan(0, 4).SequenceEqual("OggS"u8))
        {
            throw new InvalidDataException("The Wiki did not return an Ogg file.");
        }

        using var reader = new VorbisReader(new MemoryStream(ogg), closeOnDispose: true);
        var samples = new List<short>();
        var buffer = new float[reader.SampleRate * reader.Channels];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                samples.Add((short)Math.Round(Math.Clamp(buffer[i], -1f, 1f) * short.MaxValue));
            }
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int bytes = samples.Count * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)reader.Channels);
        writer.Write(reader.SampleRate);
        writer.Write(reader.SampleRate * reader.Channels * sizeof(short));
        writer.Write((short)(reader.Channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
