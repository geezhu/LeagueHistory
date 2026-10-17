using League.Managers;
using static StatisticsCheckSupport;

/// <summary>
/// The match detail manager's mode list: which queue tags allow the full detail view. Its replay paths all end in a
/// message box that would block an unattended run, so they stay out of the checks on purpose.
/// </summary>
internal static class MatchDetailManagerChecks
{
    private static readonly string[] StandardModes =
    [
        "q_420",
        "q_440",
        "q_400",
        "q_430",
        "q_450",
        "q_900",
        "q_1900",
        "q_480",
        "q_2400",
        "q_2450",
        "q_3270",
        "q_830",
        "q_840",
        "q_850",
        "q_870",
        "q_880",
        "q_890",
        "q_950",
        "q_960",
        "q_1020",
        "q_1300",
        "q_1400",
        "q_4310",
        "q_4320",
        "q_3140",
        "q_3100",
    ];

    public static void Run()
    {
        // The mode list does not touch the form, so the manager is built without one and no hotkey is ever armed.
        var manager = new MatchDetailManager(null!);

        Check(!manager.IsSummonersRiftOrAram(null!), "A missing queue tag has no detail view");
        Check(
            !manager.IsSummonersRiftOrAram("") && !manager.IsSummonersRiftOrAram(" "),
            "A blank queue tag has no detail view"
        );
        Check(StandardModes.All(manager.IsSummonersRiftOrAram), "Every standard 5v5 mode allows the detail view");
        Check(
            manager.IsSummonersRiftOrAram("q_420")
                && manager.IsSummonersRiftOrAram("q_2400")
                && manager.IsSummonersRiftOrAram("q_3100"),
            "Ranked solo, mayhem and the practice tool are among them"
        );
        Check(
            !manager.IsSummonersRiftOrAram("q_1700")
                && !manager.IsSummonersRiftOrAram("420")
                && !manager.IsSummonersRiftOrAram("q_"),
            "A tag outside the list has no detail view"
        );
        Check(!manager.IsSummonersRiftOrAram("q_420 "), "A tag with stray whitespace is not matched");

        Console.WriteLine(
            "PASS: the match detail manager's standard 5v5 mode list accepts exactly the documented queue tags."
        );
    }
}
