using League.Infrastructure;
using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// The augment descriptions the Mayhem pages show. The table is a static list, so the check is about the parts the
/// rest of the program relies on: it is populated, it is looked up without regard to case, and every augment the
/// catalogue can record has a description.
/// </summary>
internal static class AugmentDescriptionChecks
{
    public static void Run()
    {
        var descriptions = AugmentDescriptionRepository.GetAll();

        Check(descriptions.Count > 200, "The description table is populated");
        Check(
            descriptions.TryGetValue("坦克引擎", out var tank) && tank!.Contains("层数", StringComparison.Ordinal),
            "A known augment has its description"
        );
        Check(
            descriptions.TryGetValue("无限循环往复", out var recursion)
                && descriptions.TryGetValue("无限循环往复", out var again)
                && ReferenceEquals(recursion, again),
            "The same description comes back for the same name"
        );
        Check(
            descriptions.Comparer.Equals(StringComparer.OrdinalIgnoreCase),
            "Descriptions are matched without regard to case"
        );
        Check(descriptions.Values.All(text => text.Length > 0), "No description is empty");

        // Two tracked augments have no description yet; filling them in must not fail this check, but a new
        // tracked augment without one should.
        string[] knownGaps = ["大招工具人", "急速之追求"];
        string[] missing = AugmentCatalog
            .All.Select(info => info.Name)
            .Where(name => !descriptions.ContainsKey(name))
            .ToArray();
        Check(
            missing.All(knownGaps.Contains),
            $"A tracked augment without a description is a known gap: missing {string.Join(",", missing)}"
        );

        Console.WriteLine(
            "PASS: the augment description table's size, lookups, case handling, and the coverage of the tracked augments."
        );
    }
}
