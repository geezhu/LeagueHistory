using League.Clients;

namespace League.Tests.Clients;

public class SgpServersTests
{
    [Theory]
    [InlineData("NA", "NA1")]
    [InlineData("BR", "BR1")]
    [InlineData("TR", "TR1")]
    [InlineData("LAN", "LA1")]
    [InlineData("LAS", "LA2")]
    [InlineData("OCE", "OC1")]
    [InlineData("EUW1", "EUW")]
    [InlineData("JP1", "JP")]
    [InlineData(" jp ", "JP")]
    [InlineData("TENCENT", "TENCENT")]
    public void RegionId_ClientSpelling_IsTheTableSpelling(string region, string expected)
    {
        string id = SgpServers.RegionId(region);

        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("JP", "https://apne1-red.pp.sgp.pvp.net")]
    [InlineData("KR", "https://apne1-red.pp.sgp.pvp.net")]
    [InlineData("TW2", "https://apse1-red.pp.sgp.pvp.net")]
    [InlineData("OC1", "https://apse1-red.pp.sgp.pvp.net")]
    [InlineData("NA1", "https://usw2-red.pp.sgp.pvp.net")]
    [InlineData("LA2", "https://usw2-red.pp.sgp.pvp.net")]
    [InlineData("EUW", "https://euc1-red.pp.sgp.pvp.net")]
    [InlineData("TR1", "https://euc1-red.pp.sgp.pvp.net")]
    public void MatchHistoryHost_RiotRegion_IsItsRegionalHost(string region, string expected)
    {
        string? host = SgpServers.MatchHistoryHost(region);

        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("NA")]
    [InlineData("jp1")]
    public void MatchHistoryHost_AliasOrLowerCase_FindsTheRegion(string region)
    {
        string? host = SgpServers.MatchHistoryHost(region);

        Assert.NotNull(host);
    }

    [Theory]
    [InlineData("TENCENT")]
    [InlineData("EUN1")]
    [InlineData("ME1")]
    [InlineData("")]
    public void MatchHistoryHost_TencentOrRegionOutsideTheTable_IsNull(string region)
    {
        string? host = SgpServers.MatchHistoryHost(region);

        Assert.Null(host);
    }
}
