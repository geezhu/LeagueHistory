using League.Services;

namespace League.Tests.Services;

public class SetupChecklistTests
{
    private static readonly Version Bundled = new(1, 0, 0, 102);
    private static readonly Version Older = new(1, 0, 0, 101);

    [Fact]
    public void Administrator_Elevated_IsReady()
    {
        var item = SetupChecklist.Administrator(elevated: true);

        Assert.Equal(SetupItemState.Ready, item.State);
    }

    [Fact]
    public void Administrator_NotElevated_NeedsActionAndNamesTheHotkeys()
    {
        var item = SetupChecklist.Administrator(elevated: false);

        Assert.Equal(SetupItemState.NeedsAction, item.State);
        Assert.Contains("F9 / F11", item.Detail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GameBar_StillReading_IsChecking(bool gameBarChosen)
    {
        var item = SetupChecklist.GameBar(gameBarChosen, null);

        Assert.Equal(SetupItemState.Checking, item.State);
    }

    [Fact]
    public void GameBar_CurrentVersionInstalled_IsReadyWithTheVersion()
    {
        var item = SetupChecklist.GameBar(true, new GameBarPackageStatus(Bundled, Bundled));

        Assert.Equal(SetupItemState.Ready, item.State);
        Assert.Contains("1.0.0.102", item.Detail);
    }

    [Fact]
    public void GameBar_InstalledWithoutABundledPackage_IsReady()
    {
        var item = SetupChecklist.GameBar(true, new GameBarPackageStatus(null, Older));

        Assert.Equal(SetupItemState.Ready, item.State);
    }

    [Fact]
    public void GameBar_ChosenAndNotInstalled_NeedsAction()
    {
        var item = SetupChecklist.GameBar(true, new GameBarPackageStatus(Bundled, null));

        Assert.Equal(SetupItemState.NeedsAction, item.State);
    }

    [Fact]
    public void GameBar_ChosenAndOutdated_NeedsAction()
    {
        var item = SetupChecklist.GameBar(true, new GameBarPackageStatus(Bundled, Older));

        Assert.Equal(SetupItemState.NeedsAction, item.State);
    }

    [Fact]
    public void GameBar_DesktopChosenAndNotInstalled_IsOptional()
    {
        var item = SetupChecklist.GameBar(false, new GameBarPackageStatus(Bundled, null));

        Assert.Equal(SetupItemState.Optional, item.State);
    }

    [Fact]
    public void GameBar_DesktopChosenAndOutdated_IsOptional()
    {
        var item = SetupChecklist.GameBar(false, new GameBarPackageStatus(Bundled, Older));

        Assert.Equal(SetupItemState.Optional, item.State);
    }

    [Fact]
    public void VoiceModel_Available_IsReady()
    {
        var item = SetupChecklist.VoiceModel(available: true);

        Assert.Equal(SetupItemState.Ready, item.State);
    }

    [Fact]
    public void VoiceModel_Missing_IsOptionalWithItsSize()
    {
        var item = SetupChecklist.VoiceModel(available: false);

        Assert.Equal(SetupItemState.Optional, item.State);
        Assert.Contains("MiB", item.Detail);
    }
}
