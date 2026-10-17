using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// The profile picture cache. Only the paths that answer without a download are exercised here: an unusable icon
/// id, and the shared fallback picture that every caller must be able to dispose without damaging it.
/// </summary>
internal static class ProfileIconChecks
{
    public static async Task RunAsync()
    {
        var fallback = await Profileicon.GetProfileIconAsync(0);
        Check(fallback.Width > 0 && fallback.Height > 0, "An unusable icon id falls back to the default picture");

        var negative = await Profileicon.GetProfileIconAsync(-1);
        Check(!ReferenceEquals(fallback, negative), "Every fallback is its own picture, not a shared instance");
        negative.Dispose();

        var first = Profileicon.GetDefaultImageSafe();
        var second = Profileicon.GetDefaultImageSafe();
        Check(!ReferenceEquals(first, second), "Each request for the default picture makes a copy");
        Check(first.Width == second.Width, "The copies share the default size");

        first.Dispose();
        second.Dispose();
        Check(Profileicon.GetDefaultImageSafe().Width > 0, "Disposing the copies leaves the shared picture intact");
        fallback.Dispose();

        Console.WriteLine("PASS: the unusable icon id fallback and the independence of the shared default picture.");
    }
}
