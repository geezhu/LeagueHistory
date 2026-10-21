using League.Models;
using Newtonsoft.Json;

namespace League.Tests.Models;

/// <summary>
/// The settings file's shape: what a file that predates a field reads as, and what happens to fields that were removed.
///
/// The defaults live on the properties themselves rather than in a migration step, so an old file that never mentions a
/// setting gets the value the program would have used anyway. That is why the tests read JSON instead of constructing
/// the object: the question is what the file means, not what the class does when it is filled in by hand.
/// </summary>
public class LeagueConfigTests
{
    private static LeagueConfig Read(string json) => JsonConvert.DeserializeObject<LeagueConfig>(json)!;

    // ---- what an old file gets ----

    [Fact]
    public void AFileWithoutTheOverlayBackend_UsesTheDesktopWindow()
    {
        Assert.Equal("Desktop", Read("{}").LoadingOverlayBackend);
    }

    [Fact]
    public void AFileWithoutTheMasteryCache_UsesThirtyMinutes()
    {
        // The cache existed before the setting did, so an old file's silence means "the old behaviour".
        Assert.Equal(30, Read("{}").ChampionMasteryCacheMinutes);
    }

    [Fact]
    public void AFileWithoutAMicrophone_UsesTheSystemDefault()
    {
        // An empty identifier is what the settings page reads as "follow Windows".
        Assert.Equal("", Read("{}").VoiceMicrophoneId);
    }

    [Fact]
    public void AFileThatDoesNameThem_KeepsItsOwnValues()
    {
        LeagueConfig config = Read(
            """{"LoadingOverlayBackend":"GameBar","ChampionMasteryCacheMinutes":5,"VoiceMicrophoneId":"mic-1"}"""
        );

        Assert.Equal("GameBar", config.LoadingOverlayBackend);
        Assert.Equal(5, config.ChampionMasteryCacheMinutes);
        Assert.Equal("mic-1", config.VoiceMicrophoneId);
    }

    // ---- fields that were removed ----

    [Fact]
    public void AFileStillHoldingTheRemovedShortcutFields_ReadsWithoutFailing()
    {
        // Those two settings were dropped; an old file is not an error, it is just older.
        LeagueConfig config = Read(
            """{"LoadingOverlayHotkey":"Ctrl+F1","LoadingOverlayHotkeyVersion":2,"LoadingOverlayBackend":"GameBar"}"""
        );

        Assert.Equal("GameBar", config.LoadingOverlayBackend);
    }

    [Fact]
    public void SavingAFileThatHeldTheRemovedFields_DoesNotWriteThemBack()
    {
        // The point of dropping them: the next save must not keep them alive in the file.
        LeagueConfig config = Read(
            """{"LoadingOverlayHotkey":"Ctrl+F1","LoadingOverlayHotkeyVersion":2,"ChampionMasteryCacheMinutes":7}"""
        );

        string json = JsonConvert.SerializeObject(config);

        Assert.DoesNotContain("LoadingOverlayHotkey", json, StringComparison.Ordinal);
        Assert.Contains("ChampionMasteryCacheMinutes", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingAFileThatHeldTheRemovedFields_KeepsTheSettingsAroundThem()
    {
        // Losing the neighbours of a removed field would be worse than losing the field.
        LeagueConfig config = Read(
            """{"LoadingOverlayHotkey":"Ctrl+F1","LoadingOverlayBackend":"GameBar","VoiceMicrophoneId":"mic-1"}"""
        );

        LeagueConfig again = JsonConvert.DeserializeObject<LeagueConfig>(JsonConvert.SerializeObject(config))!;

        Assert.Equal("GameBar", again.LoadingOverlayBackend);
        Assert.Equal("mic-1", again.VoiceMicrophoneId);
    }

    // ---- a file that says nothing at all ----

    [Fact]
    public void AnEmptyFile_IsTheDefaultSettings()
    {
        // Reading a file that exists but holds "{}" is the same as having no file, not an error.
        Assert.NotNull(Read("{}"));
    }
}
