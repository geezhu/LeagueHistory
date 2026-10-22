using System.Drawing.Imaging;

namespace League.Services;

/// <summary>
/// The stat icons of the game's own stats panel, for the Game Bar's stat table. The game keeps them as 20-pixel cells
/// of one 512-pixel atlas (statspanel_atlas.png), which CommunityDragon serves; the atlas is downloaded once, kept on
/// disk, and cut into one small PNG per stat. Without the atlas, or when its size changes and the cells no longer fit,
/// there are no icons and the table shows the names alone.
/// </summary>
public sealed class StatIconAtlas
{
    // Lazy: the instance reads the static client below, which a field initializer here would see unset.
    private static readonly Lazy<StatIconAtlas> SharedAtlas = new(() => new StatIconAtlas());

    public static StatIconAtlas Shared => SharedAtlas.Value;

    public const int AtlasSize = 512;
    public const int IconSize = 20;

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // CommunityDragon refuses requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
        return client;
    });

    // The cell of each stat, by the key the stat table names. The panel has no icon of its own for health, the
    // resource or critical damage; they share the icon of their regeneration and of critical chance.
    private static readonly Dictionary<string, Point> Cells = new(StringComparer.Ordinal)
    {
        ["health"] = new(233, 411),
        ["healthRegen"] = new(233, 411),
        ["mana"] = new(260, 411),
        ["manaRegen"] = new(260, 411),
        ["tenacity"] = new(287, 411),
        ["ap"] = new(314, 411),
        ["lethality"] = new(233, 437),
        ["magicPen"] = new(260, 437),
        ["armor"] = new(287, 437),
        ["mr"] = new(314, 437),
        ["ad"] = new(233, 463),
        ["lifeSteal"] = new(260, 463),
        ["omnivamp"] = new(287, 463),
        ["haste"] = new(314, 463),
        ["crit"] = new(233, 489),
        ["critDamage"] = new(233, 489),
        ["moveSpeed"] = new(260, 489),
        ["attackSpeed"] = new(287, 489),
        ["range"] = new(314, 489),
    };

    private readonly HttpClient _http;
    private readonly string _path;
    private readonly string _url;
    private readonly object _gate = new();
    private Task<IReadOnlyDictionary<string, string>>? _icons;

    public StatIconAtlas(
        string? folder = null,
        HttpClient? http = null,
        string url = "https://raw.communitydragon.org/latest/game/assets/ux/lol/statspanel_atlas.png"
    )
    {
        _path = Path.Combine(
            folder
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "League",
                    "Cache",
                    "cdragon-ui"
                ),
            "statspanel_atlas.png"
        );
        _http = http ?? SharedHttp.Value;
        _url = url;
    }

    /// <summary>
    /// Each stat's icon as base64 PNG, by key; empty when the atlas cannot be read. Read once per process; a failed
    /// read is tried again by the next call.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken token)
    {
        Task<IReadOnlyDictionary<string, string>> reading;
        lock (_gate)
        {
            reading = _icons ??= ReadAsync();
        }

        var icons = await reading.WaitAsync(token).ConfigureAwait(false);
        if (icons.Count == 0)
        {
            // Forgotten only after it finished: a read failing at once would otherwise be stored after it was cleared.
            lock (_gate)
            {
                if (_icons == reading)
                {
                    _icons = null;
                }
            }
        }
        return icons;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadAsync()
    {
        IReadOnlyDictionary<string, string> icons = new Dictionary<string, string>();
        try
        {
            byte[] bytes = File.Exists(_path)
                ? await File.ReadAllBytesAsync(_path).ConfigureAwait(false)
                : await DownloadAsync().ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            using var atlas = new Bitmap(stream);
            icons = Cut(atlas);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or IOException or ArgumentException or TaskCanceledException)
        {
            // Optional decoration: the table reads the same without icons.
            AppLog.Shared.Warn(LogCategory.Data, "属性图标读取失败", ex);
            if (ex is ArgumentException)
            {
                RemoveDamagedCopy();
            }
        }

        return icons;
    }

    // A file that is not an image is downloaded again by the next call.
    private void RemoveDamagedCopy()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException ex)
        {
            AppLog.Shared.Warn(LogCategory.Data, "属性图标缓存删除失败", ex);
        }
    }

    private async Task<byte[]> DownloadAsync()
    {
        byte[] bytes = await _http.GetByteArrayAsync(_url).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
        return bytes;
    }

    /// <summary>The atlas cut into one PNG per stat, base64; empty when the atlas is not the size the cells assume.</summary>
    public static IReadOnlyDictionary<string, string> Cut(Bitmap atlas)
    {
        if (atlas.Width != AtlasSize || atlas.Height != AtlasSize)
        {
            return new Dictionary<string, string>();
        }

        var icons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, cell) in Cells)
        {
            using var icon = atlas.Clone(
                new Rectangle(cell, new Size(IconSize, IconSize)),
                PixelFormat.Format32bppArgb
            );
            using var stream = new MemoryStream();
            icon.Save(stream, ImageFormat.Png);
            icons[key] = Convert.ToBase64String(stream.ToArray());
        }
        return icons;
    }
}
