using League.ChampionSkin;
using League.Models;
using League.PrimaryElection;
using Newtonsoft.Json.Linq;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The skin browser's data: every champion from the champion list, with the skins the account owns, sorted so the
/// champion's own look comes first.
///
/// Two rules are worth stating. The champion's own look is identified both by the client's flag and by its id, which is
/// the champion id times a thousand - either is enough, so a skin that arrives without the flag is still recognised.
/// And a preferred skin only counts when it is an owned one that is not the default, because anywhere else the default
/// is what is shown anyway.
/// </summary>
public class SkinCatalogTests
{
    private static PrimaryChampion Champion(int id, string name, string title = "称号") =>
        new()
        {
            Id = id,
            Name = name,
            Title = title,
            Alias = "Alias" + id,
        };

    private static SkinThumbItem Skin(int skinId, bool isBase = false, string? name = null) =>
        new()
        {
            SkinId = skinId,
            Name = name ?? "皮肤" + skinId,
            IsBase = isBase,
        };

    private static JObject Owned(
        int skinId,
        int championId = 0,
        bool owned = true,
        string? name = null,
        bool? isBase = null
    )
    {
        var entry = new JObject
        {
            ["id"] = skinId,
            ["ownership"] = new JObject { ["owned"] = owned },
        };
        if (championId > 0)
        {
            entry["championId"] = championId;
        }

        if (name != null)
        {
            entry["name"] = name;
        }

        if (isBase.HasValue)
        {
            entry["isBase"] = isBase.Value;
        }

        return entry;
    }

    // ---- sorting ----

    [Fact]
    public void Build_TheChampionsAreSortedByName()
    {
        List<ChampionSkinCardModel> cards = SkinCatalog.Build(
            [Champion(1, "丙"), Champion(2, "甲"), Champion(3, "乙")],
            new Dictionary<int, List<SkinThumbItem>>(),
            new Dictionary<int, int>()
        );

        Assert.Equal(["丙", "乙", "甲"], cards.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    // ---- the default skin ----

    [Fact]
    public void Build_AChampionWithNoOwnedSkins_StillGetsItsOwnLook()
    {
        // Every champion has one, owned or not, so the browser always has something to draw.
        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build(
                [Champion(103, "阿狸")],
                new Dictionary<int, List<SkinThumbItem>>(),
                new Dictionary<int, int>()
            )
        );

        Assert.Equal(103000, card.DefaultSkinId);
        SkinThumbItem skin = Assert.Single(card.Skins);
        Assert.Equal(103000, skin.SkinId);
        Assert.True(skin.IsBase);
        Assert.Equal("默认", skin.Name);
    }

    [Fact]
    public void Build_AChampionWhoseOwnedSkinsLackTheDefault_AlsoGetsIt()
    {
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103001)] };

        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int>())
        );

        Assert.Equal(2, card.Skins.Count);
        Assert.Contains(card.Skins, s => s.SkinId == 103000);
    }

    [Fact]
    public void Build_AChampionThatAlreadyOwnsItsDefault_DoesNotGetASecondOne()
    {
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103000, isBase: true)] };

        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int>())
        );

        Assert.Single(card.Skins);
    }

    [Fact]
    public void Build_TheOwnLookIsSortedFirstAndTheRestById()
    {
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103002), Skin(103000), Skin(103001)] };

        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int>())
        );

        Assert.Equal([103000, 103001, 103002], card.Skins.Select(s => s.SkinId));
    }

    // ---- whether the account owns the champion ----

    [Fact]
    public void Build_AChampionTheAccountOwns_IsMarkedOwned()
    {
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103000)] };

        Assert.True(SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int>())[0].IsOwned);
    }

    [Fact]
    public void Build_AChampionTheAccountDoesNotOwn_IsNotMarked()
    {
        Assert.False(
            SkinCatalog
                .Build([Champion(103, "阿狸")], new Dictionary<int, List<SkinThumbItem>>(), new Dictionary<int, int>())[
                    0
                ]
                .IsOwned
        );
    }

    // ---- the preferred skin ----

    [Fact]
    public void Build_APreferenceForAnOwnedNonDefaultSkin_IsKept()
    {
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103001)] };

        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int> { [103] = 103001 })
        );

        Assert.Equal(103001, card.PreferredSkinId);
    }

    [Fact]
    public void Build_APreferenceForASkinTheAccountDoesNotOwn_IsIgnored()
    {
        // The widget would otherwise try to show a skin it cannot draw.
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103001)] };

        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int> { [103] = 103999 })
        );

        Assert.Equal(0, card.PreferredSkinId);
    }

    [Fact]
    public void Build_APreferenceForTheDefault_IsIgnored()
    {
        // The default is what is shown when no preference applies, so recording it says nothing.
        var owned = new Dictionary<int, List<SkinThumbItem>> { [103] = [Skin(103000), Skin(103001)] };

        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build([Champion(103, "阿狸")], owned, new Dictionary<int, int> { [103] = 103000 })
        );

        Assert.Equal(0, card.PreferredSkinId);
    }

    [Fact]
    public void Build_NoPreference_IsZero()
    {
        ChampionSkinCardModel card = Assert.Single(
            SkinCatalog.Build(
                [Champion(103, "阿狸")],
                new Dictionary<int, List<SkinThumbItem>>(),
                new Dictionary<int, int>()
            )
        );

        Assert.Equal(0, card.PreferredSkinId);
    }

    // ---- reading one entry of the inventory ----

    [Fact]
    public void AddOwnedSkin_AnOwnedSkin_IsFiledUnderItsChampion()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(103001, championId: 103));

        Assert.Equal(103001, Assert.Single(result[103]).SkinId);
    }

    [Fact]
    public void AddOwnedSkin_ASkinTheAccountDoesNotOwn_IsIgnored()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(103001, 103, owned: false));

        Assert.Empty(result);
    }

    [Fact]
    public void AddOwnedSkin_WithoutAChampionId_TheSkinIdSaysWhichChampion()
    {
        // The champion id is the skin id over a thousand, so the entry can be filed without it.
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(103001));

        Assert.True(result.ContainsKey(103));
    }

    [Fact]
    public void AddOwnedSkin_AnotherChampionsDefault_IsFiledUnderThatChampion()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(45000));

        Assert.True(result.ContainsKey(45));
    }

    [Fact]
    public void AddOwnedSkin_NoIdsAtAll_IsIgnored()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(0));

        Assert.Empty(result);
    }

    [Fact]
    public void AddOwnedSkin_TheSameSkinTwice_IsFiledOnce()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(103001, 103));
        SkinCatalog.AddOwnedSkin(result, Owned(103001, 103));

        Assert.Single(result[103]);
    }

    [Theory]
    [InlineData(103000, true)]
    [InlineData(103001, false)]
    public void AddOwnedSkin_WithoutTheClientsFlag_AnIdEndingInThousandIsTheOwnLook(int skinId, bool isBase)
    {
        // The id carries the same information as the flag, so it is used when the flag is missing.
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(skinId, 103));

        Assert.Equal(isBase, Assert.Single(result[103]).IsBase);
    }

    [Fact]
    public void AddOwnedSkin_TheClientsFlagWinsOverTheId()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(103001, 103, isBase: true));

        Assert.True(Assert.Single(result[103]).IsBase);
    }

    [Fact]
    public void AddOwnedSkin_WithoutAName_TheOwnLookIsCalledDefaultAndAnythingElseByItsId()
    {
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, Owned(103000, 103));
        SkinCatalog.AddOwnedSkin(result, Owned(103001, 103));

        Assert.Equal("默认", result[103][0].Name);
        Assert.Equal("103001", result[103][1].Name);
    }

    [Fact]
    public void AddOwnedSkin_ABlankPath_IsStoredAsNoPath()
    {
        // A blank path is not a path; leaving it as "" would make the browser try to load one.
        JObject entry = Owned(103001, 103);
        entry["tilePath"] = "   ";
        entry["splashPath"] = "  splash.png  ";
        var result = new Dictionary<int, List<SkinThumbItem>>();

        SkinCatalog.AddOwnedSkin(result, entry);

        Assert.Null(result[103][0].TilePath);
        Assert.Equal("splash.png", result[103][0].SplashPath);
    }

    // ---- filling in the image paths ----

    [Fact]
    public void ApplyChampionPaths_AMatchingEntry_FillsTheMissingPaths()
    {
        var champion = new JObject
        {
            ["skins"] = new JArray(
                new JObject
                {
                    ["id"] = 103001,
                    ["tilePath"] = "tile.png",
                    ["splashPath"] = "splash.png",
                }
            ),
        };
        SkinThumbItem skin = Skin(103001);

        SkinCatalog.ApplyChampionPaths(champion, skin);

        Assert.Equal("tile.png", skin.TilePath);
        Assert.Equal("splash.png", skin.SplashPath);
    }

    [Fact]
    public void ApplyChampionPaths_APathThatIsAlreadyThere_IsNotReplaced()
    {
        // The inventory's own paths are kept; this only fills what is missing.
        var champion = new JObject
        {
            ["skins"] = new JArray(new JObject { ["id"] = 103001, ["tilePath"] = "other.png" }),
        };
        SkinThumbItem skin = Skin(103001);
        skin.TilePath = "mine.png";

        SkinCatalog.ApplyChampionPaths(champion, skin);

        Assert.Equal("mine.png", skin.TilePath);
    }

    [Fact]
    public void ApplyChampionPaths_NoMatchingEntry_ChangesNothing()
    {
        var champion = new JObject
        {
            ["skins"] = new JArray(new JObject { ["id"] = 103999, ["tilePath"] = "other.png" }),
        };
        SkinThumbItem skin = Skin(103001);

        SkinCatalog.ApplyChampionPaths(champion, skin);

        Assert.Null(skin.TilePath);
    }

    [Fact]
    public void ApplyChampionPaths_NoSkinsList_ChangesNothing()
    {
        SkinThumbItem skin = Skin(103001);

        SkinCatalog.ApplyChampionPaths(new JObject(), skin);

        Assert.Null(skin.TilePath);
    }
}
