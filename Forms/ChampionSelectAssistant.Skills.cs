using League.Controls;
using League.Infrastructure;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private readonly Dictionary<int, JObject> _skillCache = new();

    // The banner carries the champion name, tier, win rate and mastery above the skill icons.
    private ProfileBackgroundPanel? _banner;
    private FlowLayoutPanel? _heroInfo;

    internal sealed record Skill(string Key, string Name, string Description, string Icon);

    internal static Skill[] ParseSkills(JObject champion)
    {
        var result = new List<Skill>();
        void Add(string key, JObject data)
        {
            result.Add(
                new Skill(
                    key,
                    (string?)data["name"] ?? key,
                    TooltipText.PlainText((string?)data["description"] ?? "暂无技能说明"),
                    (string?)data["abilityIconPath"] ?? (string?)data["iconPath"] ?? ""
                )
            );
        }
        if (champion["passive"] is JObject passive)
        {
            Add("被动", passive);
        }

        var spells = (champion["spells"] as JArray ?? new()).OfType<JObject>().ToArray();
        var keys = new[] { "Q", "W", "E", "R" };
        for (int i = 0; i < Math.Min(4, spells.Length); i++)
        {
            var key = ((string?)spells[i]["spellKey"])?.ToUpperInvariant();
            Add(key != null && keys.Contains(key) ? key : keys[i], spells[i]);
        }
        result = result.OrderBy(skill => Array.IndexOf(new[] { "被动", "Q", "W", "E", "R" }, skill.Key)).ToList();
        return result.ToArray();
    }

    private async Task LoadSkillsAsync(int hero, CancellationToken token)
    {
        var (section, artwork, icons, description) = CreateSkillSection(hero);
        try
        {
            var client = FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接");
            var data = await GetSkillsAsync(client, hero, token);
            var skills = ParseSkills(data);
            if (section.IsDisposed || token.IsCancellationRequested)
            {
                return;
            }
            description.Text = "暂无技能资料";
            description.Visible = skills.Length == 0;
            var cache = new ImageCacheService(client);
            _ = LoadSkillArtworkAsync(cache, artwork, data, hero, token);
            foreach (var skill in skills)
            {
                var button = CreateSkillButton(skill);
                icons.Controls.Add(button);
                _ = LoadSkillIconAsync(cache, button, skill, hero, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Hero changes retire the entire skill section.
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Assistant skills: {error}");
            if (!section.IsDisposed && !token.IsCancellationRequested)
            {
                description.Visible = true;
                description.Text = "技能资料暂不可用 · 点击重试";
                description.Cursor = Cursors.Hand;
                description.Click += (_, _) => _ = LoadHeroAsync();
            }
        }
    }

    private (
        FlowLayoutPanel Section,
        ProfileBackgroundPanel Artwork,
        FlowLayoutPanel Icons,
        Label Description
    ) CreateSkillSection(int hero)
    {
        var section = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = DipPadding(0, 0, 0, 4),
        };
        var artwork = new ProfileBackgroundPanel
        {
            Size = new Size(ContentWidth, Dip(80)),
            Padding = DipPadding(8, 4, 8, 4),
            BackColor = Color.FromArgb(16, 31, 43),
            Margin = Padding.Empty,
        };
        var title = new Label
        {
            Text = FormMain.Globals.resLoading?.GetChampionById(hero)?.Name ?? $"英雄 {hero}",
            AutoSize = true,
            Margin = Padding.Empty,
            ForeColor = Color.WhiteSmoke,
            BackColor = Color.Transparent,
        };
        var titleFont = new Font(Font.FontFamily, 12, FontStyle.Bold);
        title.Font = titleFont;
        title.Disposed += (_, _) => titleFont.Dispose();
        var info = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Dip(28),
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };
        info.Controls.Add(title);
        var icons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Dip(44),
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };
        artwork.Controls.Add(icons);
        artwork.Controls.Add(info);
        _banner = artwork;
        _heroInfo = info;
        var description = new Label
        {
            Text = "正在加载技能…",
            Visible = false,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth - Dip(4), 0),
            ForeColor = Color.FromArgb(187, 206, 214),
            Margin = DipPadding(0, 6, 0, 4),
        };
        section.Controls.AddRange(new Control[] { artwork, description });
        _body.Controls.Add(section);
        return (section, artwork, icons, description);
    }

    private async Task<JObject> GetSkillsAsync(HttpClient client, int hero, CancellationToken token)
    {
        if (!_skillCache.TryGetValue(hero, out var data))
        {
            data = JObject.Parse(await client.GetStringAsync($"/lol-game-data/assets/v1/champions/{hero}.json", token));
            token.ThrowIfCancellationRequested();
            if ((int?)data["id"] != hero)
            {
                throw new InvalidDataException("技能资料与英雄不匹配");
            }

            _skillCache[hero] = data;
        }
        return data;
    }

    private Button CreateSkillButton(Skill skill)
    {
        var button = new Button
        {
            Text = "",
            AccessibleName = skill.Key + " · " + skill.Name,
            Size = Dip(36, 36),
            Margin = DipPadding(2),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(29, 45, 61),
            BackgroundImageLayout = ImageLayout.Zoom,
        };
        _tips.SetToolTip(button, TooltipText.Format(skill.Key + " · " + skill.Name, skill.Description));
        return button;
    }

    private static async Task LoadSkillArtworkAsync(
        ImageCacheService cache,
        ProfileBackgroundPanel artwork,
        JObject data,
        int hero,
        CancellationToken token
    )
    {
        try
        {
            var path = ProfileBackgroundPanel.FindSplash(data, hero * 1000);
            if (path == null)
            {
                return;
            }
            var image = await cache.GetOrDownloadImageAsync(path, "profile-backgrounds", $"{hero * 1000}.png");
            if (artwork.IsDisposed || token.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }
            artwork.SetChampionArtwork(image);
        }
        catch (Exception error)
        {
            // Optional artwork failure leaves the usable dark skill header in place.
            System.Diagnostics.Debug.WriteLine($"Assistant skill artwork: {error}");
        }
    }

    private static async Task LoadSkillIconAsync(
        ImageCacheService cache,
        Button button,
        Skill skill,
        int hero,
        CancellationToken token
    )
    {
        if (
            string.IsNullOrEmpty(skill.Icon)
            || !skill.Icon.StartsWith("/lol-game-data/assets/", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }
        try
        {
            var image = await cache.GetOrDownloadImageAsync(skill.Icon, "skills", $"{hero}-{skill.Key}.png");
            if (button.IsDisposed || token.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }
            button.BackgroundImage = image;
            button.Disposed += (_, _) => image?.Dispose();
        }
        catch (Exception error)
        {
            // The tooltip still describes this skill when its optional asset is missing.
            System.Diagnostics.Debug.WriteLine($"Assistant skill icon: {error}");
        }
    }
}
