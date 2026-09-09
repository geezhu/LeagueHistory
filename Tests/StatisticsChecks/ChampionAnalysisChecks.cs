using League.Parsers;
using League.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ChampionAnalysisChecks
{
    public static void Run(string? output = null)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        JObject Load(string alias) => JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ChampionAnalysis", alias + ".json")));
        var reports = new Dictionary<string, League.Models.ChampionAnalysisReport>();
        foreach (string alias in new[] { "lux", "morgana", "nautilus" })
        {
            var report = ChampionAnalysisParser.Parse(Load(alias), alias,
                $"https://raw.communitydragon.org/latest/game/data/characters/{alias}/{alias}.bin.json", "latest");
            reports[alias] = report;
            Check(report.Spells.Select(s => s.Key).SequenceEqual(new[] { "Q", "W", "E", "R" }), "Use root spell order, not ability order");
            Check(report.Warnings.Any(w => w.Contains("未锁定")), "Unversioned samples explicitly marked");
            // Verify every evidence pointer really resolves to the original sample.
            var original = Load(alias);
            foreach (var value in report.Attributes.Concat(report.Spells.SelectMany(s => s.Parameters)))
            {
                if (value.Path.StartsWith("derived:")) continue;
                JToken? token = original;
                foreach (string part in value.Path.Split('/').Skip(1))
                {
                    string decoded = part.Replace("~1", "/").Replace("~0", "~");
                    token = token is JArray array ? array[int.Parse(decoded)] : token?[decoded];
                }
                Check(token != null || value.Values.All(v => v == null), "Evidence must be resolvable or unknown: " + value.Path);
            }
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, alias + ".md"), ChampionAnalysisMarkdown.Render(report));
                File.WriteAllText(Path.Combine(output, alias + ".json"), JsonConvert.SerializeObject(report, Formatting.Indented));
            }
        }
        var lux = reports["lux"];
        var q = lux.Spells[0];
        Check(q.Parameters.Single(p => p.Name == "内部施法范围").Values[1] == 10000, "Keep internal range evidence");
        Check(q.Parameters.Single(p => p.Name == "显示施法范围").Values[1] == 1175, "Keep display range distinct");
        Check(q.Parameters.Single(p => p.Name == "RootDuration").Values[1] == 2, "Lux root parameter");
        Check(Math.Abs(lux.Attributes.Single(p => p.Name.StartsWith("基础普攻周期")).Values[0]!.Value - 1 / .669) < .0001, "Base attack period units");
        Check(lux.Spells[1].Formulas.Single(p => p.Name == "TotalShieldTT") is { Resolved: false } shield
            && shield.Expression.Contains("BaseShieldAmount") && shield.Expression.Contains("缺省"), "Unverified default stat must not be silently AP");
        var morgana = reports["morgana"].Spells[3];
        Check(morgana.Parameters.Single(p => p.Name == "ChainDuration").Values[1] == 3
            && morgana.Parameters.Single(p => p.Name == "StunDuration").Values[1] == 1.5, "Do not sum chain delay and stun duration");
        Check(morgana.Warnings.Any(w => w.Contains("不计入")), "Chain duration limitation visible");
        Check(reports["nautilus"].Spells[1].Formulas.Any(f => f.Expression.Contains("ShieldHealthRatio") && !f.Resolved), "Health scaling preserved without guessing enum");

        var mutated = Load("lux");
        string rootKey = "Characters/Lux/CharacterRecords/Root";
        ((JObject)mutated[rootKey]!).Remove("baseHPModifiable");
        string spellKey = (string)mutated[rootKey]!["spells"]![0]!;
        mutated[spellKey]!["mSpell"]!["mSpellCalculations"] = JObject.Parse("""
        {"Future":{"__type":"UnknownFutureNode"},"Loop":{"__type":"GameCalculationModified","mModifiedGameCalculation":"Loop"}}
        """);
        var unknown = ChampionAnalysisParser.Parse(mutated, "lux", "fixture", "test");
        Check(unknown.Attributes.Single(v => v.Name == "基础生命").Values[0] == null, "Missing is not zero");
        Check(unknown.Spells[0].Formulas.All(f => !f.Resolved), "Unknown nodes and circular references fail closed");
        Console.WriteLine("Champion analysis checks passed: three captured champions, source paths, ranges, control duration, shield formulas, unknowns and recursion.");
        if (output != null) Console.WriteLine("Reports: " + Path.GetFullPath(output));
    }
}
