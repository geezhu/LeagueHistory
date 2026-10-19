using League.Services;
using League.Tests.Support;
using Newtonsoft.Json;

namespace League.Tests.Services;

/// <summary>
/// The recent-player search history: the twenty-entry window, the case-insensitive promotion of an id that is
/// searched again, and the JSON file that carries the list between sessions. Every history is built over a file
/// inside a temporary folder, so the user's own search-history.json is never read or written; the null path, which
/// would open that file, is never used.
/// </summary>
public class PlayerSearchHistoryTests
{
    private const string FileName = "search-history.json";

    [Fact]
    public void Limit_Constant_IsTwenty()
    {
        Assert.Equal(20, PlayerSearchHistory.Limit);
    }

    [Fact]
    public void Add_TwoIds_PutsTheMostRecentlyAddedFirst()
    {
        using var folder = new TempFolder("league-search-order");
        var history = History(folder);

        history.Add("first#123");
        history.Add("second#456");

        Assert.Equal(new[] { "second#456", "first#123" }, history.Items);
    }

    [Fact]
    public void Add_IdThatIsAlreadyTheOldest_MovesItToTheFrontWithoutDuplicatingIt()
    {
        using var folder = new TempFolder("league-search-promote");
        var history = History(folder);
        AddAll(history, ["first#123", "second#456", "third#789"]);
        // The id has to be present and away from the front, or the promotion below would prove nothing.
        Assert.Equal(new[] { "third#789", "second#456", "first#123" }, history.Items);

        history.Add("first#123");

        Assert.Equal(new[] { "first#123", "third#789", "second#456" }, history.Items);
    }

    [Theory]
    [InlineData("player#123")]
    [InlineData("PLAYER#123")]
    public void Add_IdThatDiffersOnlyInCase_KeepsTheNewestSpellingOnce(string id)
    {
        using var folder = new TempFolder("league-search-case");
        var history = History(folder);
        AddAll(history, ["Player#123", "other#456"]);
        // The earlier spelling has to be on the list, or "kept once" would not be about deduplication.
        Assert.Equal(new[] { "other#456", "Player#123" }, history.Items);

        history.Add(id);

        Assert.Equal(new[] { id, "other#456" }, history.Items);
    }

    [Fact]
    public void Add_IdWithSurroundingWhitespace_StoresTheTrimmedId()
    {
        using var folder = new TempFolder("league-search-trim");
        var history = History(folder);

        history.Add(" 玩家#123 ");

        Assert.Equal(new[] { "玩家#123" }, history.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("#123")]
    [InlineData("player#")]
    [InlineData("a#b#c")]
    [InlineData(" # ")]
    public void Add_IdThatIsNotANameAndATag_IsIgnored(string id)
    {
        using var folder = new TempFolder("league-search-invalid");
        var history = History(folder);
        history.Add("player#123");
        // A list that already holds an id is what separates "ignored" from "nothing was added at all".
        Assert.Equal(new[] { "player#123" }, history.Items);

        history.Add(id);

        Assert.Equal(new[] { "player#123" }, history.Items);
    }

    [Fact]
    public void Add_NullId_IsIgnoredAndWritesNoFile()
    {
        using var folder = new TempFolder("league-search-null");
        var history = History(folder);

        history.Add(null!);

        Assert.Empty(history.Items);
        Assert.False(File.Exists(folder.File(FileName)));
    }

    [Fact]
    public void Add_TwentyFirstId_KeepsTwentyItemsAndDropsTheOldest()
    {
        using var folder = new TempFolder("league-search-limit");
        var history = History(folder);
        AddAll(history, PlayerIds(20));
        // The list has to be full and to hold the oldest id at its far end before the next add can drop it.
        Assert.Equal(20, history.Items.Count);
        Assert.Equal("Player0#123", history.Items[19]);

        history.Add("Player20#123");

        Assert.Equal(20, history.Items.Count);
        Assert.Equal("Player20#123", history.Items[0]);
        Assert.Equal("Player1#123", history.Items[19]);
        Assert.DoesNotContain("Player0#123", history.Items);
    }

    [Fact]
    public void Add_TwentyFiveIds_KeepsTheNewestTwenty()
    {
        using var folder = new TempFolder("league-search-bounded");
        var history = History(folder);

        AddAll(history, PlayerIds(25));

        Assert.Equal(20, history.Items.Count);
        Assert.Equal("Player24#123", history.Items[0]);
        Assert.Equal("Player5#123", history.Items[19]);
        Assert.DoesNotContain("Player4#123", history.Items);
    }

    [Fact]
    public void Add_EverySave_LeavesNoTemporaryFileBehind()
    {
        using var folder = new TempFolder("league-search-temporary");
        var history = History(folder);

        history.Add("player#123");

        Assert.Empty(Directory.EnumerateFiles(folder.Root, "*.tmp"));
    }

    [Fact]
    public void Remove_IdThatIsPresent_RemovesItAndKeepsTheOrderOfTheRest()
    {
        using var folder = new TempFolder("league-search-remove");
        var history = History(folder);
        AddAll(history, ["first#123", "second#456", "third#789"]);

        history.Remove("second#456");

        Assert.Equal(new[] { "third#789", "first#123" }, history.Items);
    }

    [Theory]
    [InlineData("PLAYER#123")]
    [InlineData(" player#123 ")]
    public void Remove_IdThatDiffersOnlyInCaseOrWhitespace_RemovesIt(string id)
    {
        using var folder = new TempFolder("league-search-remove-match");
        var history = History(folder);
        AddAll(history, ["Player#123", "other#456"]);
        Assert.Equal(2, history.Items.Count);

        history.Remove(id);

        Assert.Equal(new[] { "other#456" }, history.Items);
    }

    [Fact]
    public void Remove_IdThatIsNotPresent_LeavesTheItemsUnchanged()
    {
        using var folder = new TempFolder("league-search-remove-missing");
        var history = History(folder);
        AddAll(history, ["first#123", "second#456"]);

        history.Remove("other#999");

        Assert.Equal(new[] { "second#456", "first#123" }, history.Items);
    }

    [Fact]
    public void Remove_IdFromAnEmptyHistory_LeavesItEmpty()
    {
        using var folder = new TempFolder("league-search-remove-empty");
        var history = History(folder);

        history.Remove("player#123");

        Assert.Empty(history.Items);
    }

    [Fact]
    public void Remove_IdThatIsPresent_TakesItOutOfTheFileAsWell()
    {
        using var folder = new TempFolder("league-search-remove-file");
        var history = History(folder);
        AddAll(history, ["first#123", "second#456"]);

        history.Remove("first#123");

        Assert.Equal(new[] { "second#456" }, History(folder).Items);
    }

    [Fact]
    public void Clear_ItemsPresent_LeavesAnEmptyHistoryInMemoryAndOnDisk()
    {
        using var folder = new TempFolder("league-search-clear");
        var history = History(folder);
        AddAll(history, ["first#123", "second#456"]);
        Assert.Equal(2, history.Items.Count);

        history.Clear();

        Assert.Empty(history.Items);
        Assert.True(File.Exists(folder.File(FileName)));
        Assert.Empty(History(folder).Items);
    }

    [Fact]
    public void Constructor_FileWrittenByAnEarlierSession_RestoresTheItemsInOrder()
    {
        using var folder = new TempFolder("league-search-reload");
        var history = History(folder);
        AddAll(history, ["first#123", "second#456", "third#789"]);

        var reloaded = History(folder);

        Assert.Equal(new[] { "third#789", "second#456", "first#123" }, reloaded.Items);
    }

    [Fact]
    public void Constructor_PathThatDoesNotExist_StartsWithNoItemsAndCreatesNoFile()
    {
        using var folder = new TempFolder("league-search-missing");

        var history = History(folder);

        Assert.Empty(history.Items);
        Assert.False(File.Exists(folder.File(FileName)));
    }

    [Fact]
    public void Constructor_EmptyFile_StartsWithNoItems()
    {
        using var folder = new TempFolder("league-search-blank");
        File.WriteAllText(folder.File(FileName), "");

        Assert.Empty(History(folder).Items);
    }

    [Fact]
    public void Constructor_FileHoldingJsonNull_StartsWithNoItems()
    {
        using var folder = new TempFolder("league-search-json-null");
        File.WriteAllText(folder.File(FileName), "null");

        Assert.Empty(History(folder).Items);
    }

    [Fact]
    public void Constructor_FileThatIsNotJson_StartsWithNoItems()
    {
        using var folder = new TempFolder("league-search-damaged");
        File.WriteAllText(folder.File(FileName), "invalid json");

        Assert.Empty(History(folder).Items);
    }

    [Fact]
    public void Constructor_EntriesWithSurroundingWhitespace_StoresTheTrimmedIds()
    {
        using var folder = new TempFolder("league-search-load-trim");
        WriteHistoryFile(folder, [" player#123 ", "other#456"]);

        Assert.Equal(new[] { "player#123", "other#456" }, History(folder).Items);
    }

    [Fact]
    public void Constructor_SameIdInTwoCases_KeepsTheFirstEntryOnce()
    {
        using var folder = new TempFolder("league-search-load-case");
        WriteHistoryFile(folder, ["Player#123", "player#123", "other#456"]);

        Assert.Equal(new[] { "Player#123", "other#456" }, History(folder).Items);
    }

    [Fact]
    public void Constructor_EntriesThatAreNotANameAndATag_KeepsOnlyTheValidOnes()
    {
        using var folder = new TempFolder("league-search-load-invalid");
        string[] entries = ["player#123", "invalid", "  ", "#456", "abc#", "a#b#c", "other#456"];
        WriteHistoryFile(folder, entries);
        // The file has to hold every entry as written, or the filtered result would be read as a loader bug.
        Assert.Equal(entries, ReadHistoryFile(folder));

        Assert.Equal(new[] { "player#123", "other#456" }, History(folder).Items);
    }

    [Fact]
    public void Constructor_FileWithMoreEntriesThanTheLimit_KeepsTheFirstTwenty()
    {
        using var folder = new TempFolder("league-search-load-limit");
        string[] entries = PlayerIds(25);
        WriteHistoryFile(folder, entries);
        // A file of fewer than twenty-five entries could not tell "kept twenty" from "kept everything".
        Assert.Equal(25, ReadHistoryFile(folder).Length);

        var history = History(folder);

        Assert.Equal(20, history.Items.Count);
        Assert.Equal("Player0#123", history.Items[0]);
        Assert.Equal("Player19#123", history.Items[19]);
        Assert.DoesNotContain("Player20#123", history.Items);
    }

    /// <summary>A history over the search-history file in <paramref name="folder"/>, which nothing creates by itself.</summary>
    private static PlayerSearchHistory History(TempFolder folder) => new(folder.File(FileName));

    /// <summary>Writes the JSON array the search-history file holds, as the loader expects to read it.</summary>
    private static void WriteHistoryFile(TempFolder folder, IReadOnlyList<string> entries) =>
        File.WriteAllText(folder.File(FileName), JsonConvert.SerializeObject(entries));

    /// <summary>The entries the search-history file holds right now, read the way the loader reads them.</summary>
    private static string[] ReadHistoryFile(TempFolder folder) =>
        JsonConvert.DeserializeObject<string[]>(File.ReadAllText(folder.File(FileName))) ?? [];

    /// <summary>Adds every id in the given order; a test about the twenty-entry window needs a full list first.</summary>
    private static void AddAll(PlayerSearchHistory history, IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            history.Add(id);
        }
    }

    /// <summary>The ids Player0#123 upward, so a test can name the entry it expects to be dropped.</summary>
    private static string[] PlayerIds(int count) =>
        Enumerable.Range(0, count).Select(index => $"Player{index}#123").ToArray();
}
