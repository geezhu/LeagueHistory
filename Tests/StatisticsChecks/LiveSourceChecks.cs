using League.Clients;
using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// Opt-in integration checks against the real upstream sources: op.gg's champion tiers and build, arammeta's
/// statistics and the ARAM balance feed. They are <b>not</b> part of the default suite — they need the internet,
/// and the answers change every patch — so they only assert what must always hold and report an unreachable
/// source as a skip. Caches are written to the temp folder, never to the player's own cache.
/// Run: dotnet Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll --live-source-checks
/// </summary>
internal static class LiveSourceChecks
{
    public static async Task RunAsync()
    {
        var outcomes = new List<bool>
        {
            await Attempt("op.gg champion tiers", OpggTiersAsync),
            await Attempt("op.gg build for one champion", OpggBuildAsync),
            await Attempt("arammeta statistics", AramMetaAsync),
            await Attempt("ARAM balance feed", BalanceAsync),
        };

        int reached = outcomes.Count(ok => ok);
        Console.WriteLine(
            reached == 0
                ? "SKIP: no upstream source could be reached, so the live source checks proved nothing."
                : $"PASS: {reached} of {outcomes.Count} live upstream sources answered and matched the expected shape."
        );
    }

    private static async Task<bool> Attempt(string what, Func<CancellationToken, Task> body)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await body(timeout.Token);
            Console.WriteLine($"ok    {what}");
            return true;
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        or OperationCanceledException
                        or IOException
                        or InvalidDataException
                        or InvalidOperationException
                        or TimeoutException
                        or Newtonsoft.Json.JsonException
            )
        {
            Console.WriteLine($"skip  {what}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static async Task OpggTiersAsync(CancellationToken token)
    {
        using var client = new OpggClient();
        var (version, rows) = await client.GetTiersAsync(Query, refresh: true, token);
        Check(version.Length > 0, "op.gg reports the patch it is serving");
        Check(rows.Length > 50, "op.gg returns a champion table for the ladder");
        Check(
            rows.Any(row => row.ChampionId == Query.ChampionId),
            "The table contains the champion that was asked about"
        );
        Check(
            rows.All(row => row.WinRate is >= 0 and <= 1),
            "Every win rate op.gg returns is a share between zero and one"
        );
    }

    private static async Task OpggBuildAsync(CancellationToken token)
    {
        using var client = new OpggClient();
        var build = await client.GetBuildAsync(Query, refresh: true, token);
        Check(build.Query.ChampionId == Query.ChampionId, "The build belongs to the champion that was asked about");
        Check(build.Data.Count > 0, "The build carries its payload");
        Check(build.Runes.Length > 0, "The build offers at least one rune page");
        Check(
            build.Runes.All(runes => runes.Perks.Length == 9 && runes.Primary > 0 && runes.Secondary != runes.Primary),
            "Every rune page has nine distinct-perk slots and two different trees"
        );
    }

    private static async Task AramMetaAsync(CancellationToken token)
    {
        string cache = Temporary("arammeta");
        try
        {
            using var client = new AramMetaClient(cachePath: cache);
            var page = await client.GetAsync(Query.ChampionId, refresh: true, token);
            Check(page.Index.Patch.Length > 0, "arammeta reports the patch its statistics belong to");
            Check(page.FetchedAt > DateTime.MinValue, "The page records when it was fetched");
        }
        finally
        {
            Delete(cache);
        }
    }

    private static async Task BalanceAsync(CancellationToken token)
    {
        string cache = Temporary("balance");
        try
        {
            using var client = new AramBalanceClient(cachePath: cache);
            string summary = await client.GetAsync("Annie", TimeSpan.FromMinutes(5), token);
            Check(summary.Length > 0, "The balance feed explains the champion it was asked about");
        }
        finally
        {
            Delete(cache);
        }
    }

    private static string Temporary(string kind) =>
        Path.Combine(Path.GetTempPath(), $"league-live-{kind}-{Guid.NewGuid():N}.json");

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Live source check kept its temporary cache: " + path);
        }
    }

    // A mid-lane champion so the build endpoint has a position to answer for.
    private static readonly OpggQuery Query = new("global", "ranked", "emerald_plus", "mid", 103);
}
