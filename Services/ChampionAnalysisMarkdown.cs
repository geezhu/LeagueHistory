using League.Models;
using System.Globalization;
using System.Text;

namespace League.Services;

public static class ChampionAnalysisMarkdown
{
    public static string Render(ChampionAnalysisReport report)
    {
        var text = new StringBuilder($"# {report.Alias} 数据提取验证报告\n\n");
        text.AppendLine($"来源：{report.Source}\n\n版本标识：{report.Version}；解析器：{Parsers.ChampionAnalysisParser.SchemaVersion}。\n");
        text.AppendLine("这是字段与公式验证报告，尚不是阵容评分或实战模拟。路径使用 JSON Pointer。\n");
        foreach (string warning in report.Warnings) text.AppendLine("- " + warning);
        text.AppendLine("\n## 基础属性\n");
        Values(report.Attributes);
        foreach (var spell in report.Spells)
        {
            text.AppendLine($"\n## {spell.Key}\n\n目标类型：{spell.Targeting}。目标类型不代表必中。\n");
            text.AppendLine("标签：" + string.Join("、", spell.Tags) + "\n");
            Values(spell.Parameters);
            text.AppendLine("\n### 公式\n");
            foreach (var formula in spell.Formulas)
                text.AppendLine($"- **{formula.Name}**：{formula.Expression}；{(formula.Resolved ? "结构已解析，等级及语义仍需验证" : "未完全解析，不求值")}。\n  来源：`{formula.Path}`\n");
            foreach (string warning in spell.Warnings) text.AppendLine("- " + warning);
        }
        return text.ToString();

        void Values(IReadOnlyList<AnalysisValue> values)
        {
            text.AppendLine("| 项目 | 值（数组为原始索引） | 单位 | 来源字段 |\n| --- | --- | --- | --- |");
            foreach (var value in values)
                text.AppendLine($"| {value.Name} | {string.Join(", ", value.Values.Select(v => v?.ToString("0.####", CultureInfo.InvariantCulture) ?? "未知"))} | {value.Unit} | `{value.Path}` |");
        }
    }
}
