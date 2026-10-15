using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// Enemies' new survival items and their sounds: first sight is silent, a new item sounds once, a sold item bought
/// again sounds again, the setting silences it, and a cached sound plays without the network. No audio is played.
/// </summary>
internal static class SurvivalSoundChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        var alerts = new SurvivalItemAlerts();
        SurvivalInventory Enemy(string key, params int[] items) => new(key, items, Audible: true);
        SurvivalInventory Quiet(string key, params int[] items) => new(key, items, Audible: false);
        Check(
            alerts.Update([Enemy("a", 3157), Enemy("b")]) == null,
            "Items already held when a player is first seen are not announced"
        );
        Check(
            alerts.Update([Enemy("a", 3157), Enemy("b", 3026)]) == 3026,
            "A new survival item sounds with its own id"
        );
        Check(alerts.Update([Enemy("a", 3157), Enemy("b", 3026)]) == null, "An item sounds only once");
        Check(
            alerts.Update([Enemy("a"), Enemy("b", 3026)]) == null
                && alerts.Update([Enemy("a", 3157), Enemy("b", 3026)]) == 3157,
            "A sold item bought again is a new purchase"
        );
        Check(
            alerts.Update([Quiet("a", 3157, 3102), Enemy("b", 3026)]) == null
                && alerts.Update([Enemy("a", 3157, 3102), Enemy("b", 3026)]) == null,
            "A team turned off is remembered, not played later when turned on"
        );
        Check(
            alerts.Update([Quiet("a", 3157, 3102, 3053), Enemy("b", 3026, 3193)]) == 3193,
            "Only the audible team's purchase sounds"
        );
        alerts.Reset();
        Check(alerts.Update([Enemy("a", 3157)]) == null, "A new game starts silent again");

        Check(
            ControlSettings.Parse(JObject.Parse("""{"protocol":1,"survivalSound":false,"allySound":true,"test":3}"""))
                is { EnemySound: false, AllySound: true, Test: 3 }
                && ControlSettings.Parse(JObject.Parse("""{"protocol":1,"survivalSound":true,"test":3}"""))
                    is { EnemySound: true, AllySound: false }
                && ControlSettings.Parse(JObject.Parse("""{"protocol":1,"survivalSound":"no","test":3}""")) == null
                && ControlSettings.Default is { EnemySound: true, AllySound: false },
            "Each team's setting is read; an older widget's file is the enemy setting; enemies sound by default"
        );
        Check(
            SurvivalItemsHaveSounds() && SurvivalItems.BaseId(223157) == 3157,
            "Every survival item has a sound, mode copies included"
        );
        Task.Run(CacheAsync).GetAwaiter().GetResult();
        Console.WriteLine("PASS: survival item purchases, sound setting and cached item sounds; no audio played.");
    }

    private static bool SurvivalItemsHaveSounds() =>
        new[] { 3157, 2420, 3026, 3139, 3140, 3102, 3814, 4632, 3053, 6673, 3156, 3155, 3193 }.All(
            ItemSoundLibrary.Files.ContainsKey
        );

    // A cached sound is read without the network; an item without a sound, or a page instead of Ogg, is refused.
    private static async Task CacheAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "league-item-sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            byte[] wave = "RIFF-cached"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Combine(folder, "Guardian_Angel_passive_SFX.wav"), wave);
            var library = new ItemSoundLibrary(folder);
            Check(
                (await library.LoadAsync(223026, CancellationToken.None))?.SequenceEqual(wave) == true
                    && await library.LoadAsync(1001, CancellationToken.None) == null,
                "Cached sounds are read by base id; items without a sound have none"
            );
            bool refused = false;
            try
            {
                ItemSoundLibrary.ToWave("<html>please wait</html>"u8.ToArray());
            }
            catch (InvalidDataException)
            {
                refused = true;
            }

            Check(refused, "A page returned instead of the Ogg file is not decoded or cached");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
