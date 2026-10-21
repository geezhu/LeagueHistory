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

    [Fact]
    public void Dispose_Twice_IsHarmless()
    {
        // A row is disposed where it was created, but a caller that already released it must not get an exception
        // from the second call - which is what the guard inside Dispose is for.
        MatchInfo match = new();

        match.Dispose();
        match.Dispose();
    }
}
