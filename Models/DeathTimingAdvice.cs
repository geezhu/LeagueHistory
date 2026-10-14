using Newtonsoft.Json.Linq;

namespace League.Models;

public enum DeathTimingVerdict
{
    Good,
    Okay,
    Avoid,
}

/// <summary>One factor of the advice; a positive weight favours dying now, a negative one argues against it.</summary>
public sealed record DeathTimingFactor(int Weight, string Text);

/// <summary>
/// Whether dying now to shop is a good trade. <see cref="Reasons"/> are the deciding factors shown on the wave
/// page; <see cref="Factors"/> hold every factor for the tooltip and the diagnostics.
/// </summary>
public sealed record DeathTimingAdvice(DeathTimingVerdict Verdict, string[] Reasons, DeathTimingFactor[] Factors)
{
    public JObject ToJson() =>
        new()
        {
            ["verdict"] = Verdict switch
            {
                DeathTimingVerdict.Good => "good",
                DeathTimingVerdict.Okay => "okay",
                _ => "avoid",
            },
            ["reasons"] = new JArray(Reasons),
            ["details"] = new JArray(Factors.Select(Describe)),
        };

    public static string Describe(DeathTimingFactor factor) => Sign(factor.Weight) + factor.Text;

    private static string Sign(int weight) =>
        weight > 0 ? "＋ "
        : weight < 0 ? "－ "
        : "· ";
}
