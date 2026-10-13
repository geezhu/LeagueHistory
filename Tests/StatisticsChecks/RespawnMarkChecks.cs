using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>Respawn mark chimes: transitions, the sample chime, file validation and the WAV tones. No audio plays.</summary>
internal static class RespawnMarkChecks
{
    public static void Run()
    {
        CheckTransitions();
        CheckSampleChime();
        CheckParsing();
        CheckTones();
        Console.WriteLine("PASS: respawn mark chimes, sample chime, marks file validation and tones. No audio played.");
    }

    private static void CheckTransitions()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);
        void Poll(bool dead, double seconds, bool sound = true) =>
            sounds.Update([new RespawnMarkState("neeko", dead, seconds)], sound, 0);

        Poll(false, 0);
        Check(played.Count == 0, "Marking a living champion is silent");
        Poll(true, 23);
        Poll(true, 22);
        Check(played.Count == 0, "A death itself is silent");
        Poll(true, 5);
        Poll(true, 3);
        Check(played.SequenceEqual([RespawnTone.Soon]), "Five seconds left chimes once");
        Poll(false, 0);
        Poll(false, 0);
        Check(played.Last() == RespawnTone.Revive && played.Count == 2, "Respawn chimes once");

        played.Clear();
        Poll(true, 4);
        Check(played.SequenceEqual([RespawnTone.Soon]), "A short death timer reaches the warning at once");
        Poll(false, 0, sound: false);
        Check(played.Count == 1, "Muted marks stay silent");

        played.Clear();
        sounds.Update([], true, 0);
        sounds.Update([new RespawnMarkState("neeko", true, 20)], true, 0);
        Check(played.Count == 0, "Re-marking a dead champion does not replay the death chime");
    }

    private static void CheckSampleChime()
    {
        var played = new List<RespawnTone>();
        using var sounds = new RespawnMarkSounds(played.Add);
        sounds.Update([], true, 0);
        Check(played.Count == 0, "Reading the first marks file is silent");
        sounds.Update([], true, 1);
        Check(played.SequenceEqual([RespawnTone.Soon]), "Turning sound on plays one sample chime");
        sounds.Update([], false, 2);
        Check(played.Count == 1, "No sample while sound is off");
    }

    private static void CheckParsing()
    {
        JObject Marks(object slots, object heroes, string session = "s") =>
            new()
            {
                ["protocol"] = 1,
                ["session"] = session,
                ["slots"] = JToken.FromObject(slots),
                ["heroes"] = JToken.FromObject(heroes),
                ["sound"] = true,
                ["test"] = 3,
            };
        var marks = RespawnMarks.Parse(Marks(new[] { 6, 8 }, new[] { 518, 22 }), "s");
        Check(marks is { Sound: true, Test: 3 } && marks.Slots.SequenceEqual([6, 8]), "Marks parse");
        Check(RespawnMarks.Parse(Marks(new[] { 6 }, new[] { 518 }, "old"), "s") == null, "Another match is ignored");
        Check(RespawnMarks.Parse(Marks(new[] { 10 }, new[] { 518 }), "s") == null, "Slots stay within ten cards");
        Check(RespawnMarks.Parse(Marks(new[] { 1, 2 }, new[] { 518 }), "s") == null, "Every slot names a champion");
    }

    private static void CheckTones()
    {
        byte[] revive = RespawnMarkSounds.Tone(660, 990);
        Check(
            revive.Length > 44
                && System.Text.Encoding.ASCII.GetString(revive, 0, 4) == "RIFF"
                && BitConverter.ToInt32(revive, 40) == revive.Length - 44,
            "Tones are complete WAV files"
        );
        Check(RespawnMarkSounds.Tone(988).Length < revive.Length, "The warning is a single note");
    }
}
