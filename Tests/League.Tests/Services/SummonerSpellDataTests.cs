using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Data Dragon's summoner spells: the two pure parsers, and the list, the icons and the mode cooldowns the loader
/// reads out of its version's cache folder. Only cached reads are covered — the loader fetches through a private
/// static <c>HttpClient</c> with no seam, so the cache-miss paths cannot be exercised without reaching the CDN. The
/// expected values come from Tests/StatisticsChecks/SummonerTimerChecks.cs and LoadingOverlayManagerChecks.cs.
/// </summary>
public sealed class SummonerSpellDataTests : IDisposable
{
    private const string Version = "16.20.1";
    private const string ListFile = "summoner.json";
    private const string ModesFile = "modes-r1.json";
    private const string FlashIcon = "SummonerFlash.png";
    private const string CachedList =
        """{"data":{"SummonerFlash":{"name":"闪现","cooldown":[300],"image":{"full":"SummonerFlash.png"}},"SummonerSnowball":{"name":"雪球","cooldown":[80],"image":{"full":"Summoner_Mark.png"}}}}""";
    private readonly TempFolder _folder = new("summoner-spells");

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(
        """{"data":{"SummonerFlash":{"name":"闪现","cooldown":[300],"image":{"full":"SummonerFlash.png"}}}}""",
        "SummonerFlash",
        "闪现",
        300.0,
        "SummonerFlash.png"
    )]
    [InlineData(
        """{"data":{"SummonerFlash":{"cooldown":[300],"image":{"full":"SummonerFlash.png"}}}}""",
        "SummonerFlash",
        "SummonerFlash",
        300.0,
        "SummonerFlash.png"
    )]
    [InlineData(
        """{"data":{"SummonerFlash":{"name":null,"cooldown":[80]}}}""",
        "SummonerFlash",
        "SummonerFlash",
        80.0,
        ""
    )]
    [InlineData("""{"data":{"SummonerSmite":{"cooldown":[12.5]}}}""", "SummonerSmite", "SummonerSmite", 12.5, "")]
    [InlineData("""{"data":{"SummonerSmite":{"cooldown":[300,400]}}}""", "SummonerSmite", "SummonerSmite", 300.0, "")]
    [InlineData("""{"data":{"SummonerSmite":{"cooldown":[0]}}}""", "SummonerSmite", "SummonerSmite", 0.0, "")]
    [InlineData("""{"data":{"SummonerSmite":{"cooldown":[-7]}}}""", "SummonerSmite", "SummonerSmite", 0.0, "")]
    [InlineData(
        """{"data":{"SummonerSmite":{"cooldown":[80],"image":{}}}}""",
        "SummonerSmite",
        "SummonerSmite",
        80.0,
        ""
    )]
    public void Parse_EntryWithANumberCooldown_KeepsItsInternalNameNameCooldownAndIcon(
        string json,
        string id,
        string name,
        double cooldown,
        string image
    )
    {
        var list = JObject.Parse(json);

        var spells = SummonerSpellData.Parse(list);

        Assert.Equal(1, ((JObject)list["data"]!).Count);
        var spell = Assert.Single(spells);
        Assert.Equal(id, spell.Key);
        Assert.Equal(id, spell.Value.Id);
        Assert.Equal(name, spell.Value.Name);
        Assert.Equal(cooldown, spell.Value.Cooldown);
        Assert.Equal(image, spell.Value.Image);
    }

    [Theory]
    [InlineData("""{"data":{"SummonerBroken":{"name":"坏数据","cooldown":["x"]}}}""")]
    [InlineData("""{"data":{"SummonerBroken":{"name":"坏数据","cooldown":[]}}}""")]
    [InlineData("""{"data":{"SummonerBroken":{"cooldown":null}}}""")]
    [InlineData("""{"data":{"SummonerBroken":{"cooldown":[null]}}}""")]
    [InlineData("""{"data":{"SummonerBroken":{"cooldown":300}}}""")]
    [InlineData("""{"data":{"SummonerBroken":{}}}""")]
    [InlineData("""{"data":{"SummonerBroken":"not an entry"}}""")]
    public void Parse_EntryWithoutANumberCooldown_IsSkipped(string json)
    {
        var list = JObject.Parse(json);

        var spells = SummonerSpellData.Parse(list);

        Assert.Equal(1, ((JObject)list["data"]!).Count);
        Assert.Empty(spells);
    }

    [Theory]
    [InlineData("{}", 0)]
    [InlineData("""{"data":{}}""", 1)]
    [InlineData("""{"data":null}""", 1)]
    [InlineData("""{"data":[]}""", 1)]
    [InlineData("""{"data":"x"}""", 1)]
    public void Parse_ListWithoutASpellObject_ReturnsNothing(string json, int listProperties)
    {
        var list = JObject.Parse(json);

        var spells = SummonerSpellData.Parse(list);

        Assert.Equal(listProperties, list.Count);
        Assert.Empty(spells);
    }

    [Fact]
    public void Parse_KeysThatDifferInCase_AreKeptApart()
    {
        var list = JObject.Parse("""{"data":{"SummonerFlash":{"cooldown":[300]},"summonerflash":{"cooldown":[80]}}}""");

        var spells = SummonerSpellData.Parse(list);

        Assert.Equal(2, ((JObject)list["data"]!).Count);
        Assert.Equal(2, spells.Count);
        Assert.Equal(300.0, spells["SummonerFlash"].Cooldown);
        Assert.Equal(80.0, spells["summonerflash"].Cooldown);
    }

    [Fact]
    public void ParseModes_SharedSpells_KeepOnlyNamedOverridesOfSummonerSpells()
    {
        var shared = JObject.Parse(
            """
            {
              "Shared/Spells/SummonerExhaust": { "mSpell": { "{f9c2333e}": {
                "__type": "SpellEffectAmount",
                "ARAM": { "value": [290, 300] },
                "{12ab34cd}": { "value": [1] } } } },
              "Shared/Spells/SummonerSmite": { "mSpell": { "{f9c2333e}": { "URF": { "value": [10.0] } } } },
              "Shared/Spells/SummonerFlash": { "mSpell": { "cooldownTime": [300] } },
              "Shared/Spells/SummonerHeal": { "mSpell": { "{f9c2333e}": {} } },
              "Shared/Spells/SummonerGhost": { "mSpell": { "{f9c2333e}": { "ARAM": { "value": [0] } } } },
              "Shared/Spells/HowlingAbyssAura": { "mSpell": { "{f9c2333e}": { "ARAM": { "value": [1] } } } }
            }
            """
        );

        var modes = SummonerSpellData.ParseModes(shared);

        Assert.Equal(6, shared.Count);
        Assert.Equal(["SummonerExhaust", "SummonerSmite"], modes.Keys.Order());
        Assert.Equal(new KeyValuePair<string, double>("aram", 290.0), Assert.Single(modes["SummonerExhaust"]));
        Assert.Equal(10.0, modes["SummonerSmite"]["urf"]);
    }

    [Theory]
    [InlineData("""{"ARAM":{"value":[290,300]}}""", "aram", 290.0)]
    [InlineData("""{"URF":{"value":[10.0]}}""", "urf", 10.0)]
    [InlineData("""{"Aram":{"value":[12.5]}}""", "aram", 12.5)]
    [InlineData("""{"aram":{"value":[70]}}""", "aram", 70.0)]
    [InlineData("""{"kiwi":{"value":[1]}}""", "kiwi", 1.0)]
    public void ParseModes_PositiveModeValue_IsKeptUnderTheLowerCaseMode(string modesJson, string mode, double value)
    {
        var shared = SharedSpells(modesJson);

        var modes = SummonerSpellData.ParseModes(shared);

        Assert.Equal(1, shared.Count);
        var spell = Assert.Single(modes);
        Assert.Equal("SummonerExhaust", spell.Key);
        Assert.Equal(new KeyValuePair<string, double>(mode, value), Assert.Single(spell.Value));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"ARAM":{"value":[]}}""")]
    [InlineData("""{"ARAM":{"value":[0]}}""")]
    [InlineData("""{"ARAM":{"value":[-1]}}""")]
    [InlineData("""{"ARAM":{"value":"290"}}""")]
    [InlineData("""{"ARAM":{"value":290}}""")]
    [InlineData("""{"ARAM":{}}""")]
    [InlineData("""{"__type":"SpellEffectAmount"}""")]
    [InlineData("""{"{12ab34cd}":{"value":[1]}}""")]
    public void ParseModes_ModeWithoutAPositiveNumber_DropsTheWholeSpell(string modesJson)
    {
        var shared = SharedSpells(modesJson);

        var modes = SummonerSpellData.ParseModes(shared);

        Assert.Equal(1, shared.Count);
        Assert.Empty(modes);
    }

    [Fact]
    public async Task LoadAsync_CachedList_ReadsTheSpellsOfItsVersionFolder()
    {
        WriteCache(ListFile, CachedList);
        var data = new SummonerSpellData(_folder.Root, Version);

        var spells = await data.LoadAsync(CancellationToken.None);

        Assert.Equal(CachedList, await File.ReadAllTextAsync(CachePath(ListFile), CancellationToken.None));
        Assert.Equal(2, spells.Count);
        Assert.Equal("闪现", spells["SummonerFlash"].Name);
        Assert.Equal(300.0, spells["SummonerFlash"].Cooldown);
        Assert.Equal("Summoner_Mark.png", spells["SummonerSnowball"].Image);
    }

    [Fact]
    public async Task LoadAsync_CachedListWithoutSpells_ReturnsNothing()
    {
        WriteCache(ListFile, """{"data":{}}""");
        var data = new SummonerSpellData(_folder.Root, Version);

        var spells = await data.LoadAsync(CancellationToken.None);

        Assert.Empty(spells);
    }

    [Fact]
    public async Task LoadAsync_CachedListThatIsNotJson_ReportsAJsonError()
    {
        WriteCache(ListFile, "not json");
        var data = new SummonerSpellData(_folder.Root, Version);

        await Assert.ThrowsAnyAsync<JsonException>(() => data.LoadAsync(CancellationToken.None));

        Assert.Equal("not json", await File.ReadAllTextAsync(CachePath(ListFile), CancellationToken.None));
    }

    [Fact]
    public async Task LoadModesAsync_CachedOverrides_ReadsTheModesOfItsVersionFolder()
    {
        WriteCache(ModesFile, """{"SummonerExhaust":{"aram":290}}""");
        var data = new SummonerSpellData(_folder.Root, Version);

        var modes = await data.LoadModesAsync(CancellationToken.None);

        Assert.Equal(1, Assert.Single(modes).Value.Count);
        Assert.Equal(290.0, modes["SummonerExhaust"]["aram"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task LoadModesAsync_CachedFileWithoutOverrides_ReturnsNothing(string json)
    {
        WriteCache(ModesFile, json);
        var data = new SummonerSpellData(_folder.Root, Version);

        var modes = await data.LoadModesAsync(CancellationToken.None);

        Assert.Equal(json, await File.ReadAllTextAsync(CachePath(ModesFile), CancellationToken.None));
        Assert.Empty(modes);
    }

    [Fact]
    public async Task LoadModesAsync_CachedOverridesThatAreNotJson_ReportsAJsonError()
    {
        WriteCache(ModesFile, "not json");
        var data = new SummonerSpellData(_folder.Root, Version);

        await Assert.ThrowsAnyAsync<JsonException>(() => data.LoadModesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IconAsync_CachedIcon_ReturnsTheIconItselfAsBase64()
    {
        byte[] png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        WriteCache(FlashIcon, png);
        var data = new SummonerSpellData(_folder.Root, Version);

        string icon = await data.IconAsync(Flash(), CancellationToken.None);

        Assert.Equal(png, await File.ReadAllBytesAsync(CachePath(FlashIcon), CancellationToken.None));
        Assert.Equal(Convert.ToBase64String(png), icon);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SummonerFlash.jpg")]
    [InlineData("SummonerFlash.PNG")]
    [InlineData("Summoner Flash.png")]
    [InlineData("Summoner-Flash.png")]
    [InlineData("SummonerFlash.png.exe")]
    [InlineData("../SummonerFlash.png")]
    [InlineData(@"..\SummonerFlash.png")]
    [InlineData("sub/SummonerFlash.png")]
    public async Task IconAsync_NameThatIsNotAPlainPng_ReturnsNothingWithoutReadingTheCache(string image)
    {
        var data = new SummonerSpellData(Path.Combine(_folder.Root, "missing"), Version);

        string icon = await data.IconAsync(Flash() with { Image = image }, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_folder.Root, "*", SearchOption.AllDirectories));
        Assert.Equal("", icon);
    }

    private static SummonerSpellInfo Flash() => new("SummonerFlash", "闪现", 300, FlashIcon);

    private static JObject SharedSpells(string modesJson) =>
        new()
        {
            ["Shared/Spells/SummonerExhaust"] = new JObject
            {
                ["mSpell"] = new JObject { ["{f9c2333e}"] = JObject.Parse(modesJson) },
            },
        };

    private string CachePath(string name) => Path.Combine(_folder.Root, Version, name);

    private void WriteCache(string name, string text)
    {
        Directory.CreateDirectory(Path.Combine(_folder.Root, Version));
        File.WriteAllText(CachePath(name), text);
    }

    private void WriteCache(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Path.Combine(_folder.Root, Version));
        File.WriteAllBytes(CachePath(name), bytes);
    }
}
