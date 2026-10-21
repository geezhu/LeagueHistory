using League.Models;

namespace League.Tests.Models;

/// <summary>The figures a match row derives from the raw score line.</summary>
public class MatchInfoTests
{
    [Theory]
    [InlineData(12, 2, 18, 15.0)]
    [InlineData(4, 7, 6, 10 / 7.0)]
    [InlineData(5, 0, 3, 8.0)]
    [InlineData(0, 0, 0, 0.0)]
    public void KdaRatio_ScoreLine_DividesTakedownsByDeathsCountingNoneAsOne(
        int kills,
        int deaths,
        int assists,
        double expected
    )
    {
        using var match = new MatchInfo
        {
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
        };

        double ratio = match.KdaRatio;

        Assert.Equal(expected, ratio, 6);
    }
}
