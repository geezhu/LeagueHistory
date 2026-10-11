namespace League.Models;

/// <summary>Extraction evidence, not yet a normalized combat simulation.</summary>
public sealed record ChampionAnalysisReport(
    string Alias,
    string Source,
    string Version,
    IReadOnlyList<AnalysisValue> Attributes,
    IReadOnlyList<SpellAnalysis> Spells,
    IReadOnlyList<string> Warnings
);

public sealed record AnalysisValue(string Name, string Unit, IReadOnlyList<double?> Values, string Path);

public sealed record AnalysisFormula(string Name, string Expression, bool Resolved, string Path);

public sealed record SpellAnalysis(
    string Key,
    string Path,
    string Targeting,
    IReadOnlyList<AnalysisValue> Parameters,
    IReadOnlyList<AnalysisFormula> Formulas,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Warnings
);
