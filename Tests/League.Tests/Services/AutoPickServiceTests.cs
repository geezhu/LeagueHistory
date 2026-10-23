using League.Models;
using League.Services;

namespace League.Tests.Services;

public class AutoPickServiceTests
{
    [Theory]
    [InlineData(4310)]
    [InlineData(4320)]
    public void IsEnabledFor_ClassicRiftWithItsSwitchOn_IsEnabled(int queueId)
    {
        var config = new LeagueConfig { EnablePreliminaryInClassic = true };

        Assert.True(AutoPickService.IsEnabledFor(config, queueId));
    }

    [Theory]
    [InlineData(830)]
    [InlineData(840)]
    [InlineData(850)]
    [InlineData(870)]
    [InlineData(880)]
    [InlineData(890)]
    public void IsEnabledFor_CoopVsAiWithItsSwitchOn_IsEnabled(int queueId)
    {
        var config = new LeagueConfig { EnablePreliminaryInBot = true };

        Assert.True(AutoPickService.IsEnabledFor(config, queueId));
    }

    [Theory]
    [InlineData(4310)]
    [InlineData(850)]
    public void IsEnabledFor_TheNewModesWithTheirSwitchesOff_IsDisabled(int queueId)
    {
        var config = new LeagueConfig();

        Assert.False(AutoPickService.IsEnabledFor(config, queueId));
    }

    [Fact]
    public void IsEnabledFor_ClassicRiftWithOnlyTheCoopSwitchOn_IsDisabled()
    {
        var config = new LeagueConfig { EnablePreliminaryInBot = true };

        Assert.False(AutoPickService.IsEnabledFor(config, 4320));
    }

    [Fact]
    public void IsEnabledFor_AnUnknownQueue_IsDisabled()
    {
        var config = new LeagueConfig
        {
            EnablePreliminaryInClassic = true,
            EnablePreliminaryInBot = true,
            EnablePreliminaryInNormal = true,
        };

        Assert.False(AutoPickService.IsEnabledFor(config, 1700));
    }
}
