using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Services;

/// <summary>
/// A piece of a rendered tooltip. Text pieces carry <see cref="Text"/>; number pieces carry one formatted value per
/// rank in <see cref="Values"/> (a single value when every rank shows the same). <see cref="Style"/> is the damage
/// or keyword kind the tooltip marks it with: "magic", "physical", "true", "heal", "shield", "speed", "key", or "".
/// </summary>
public sealed record TooltipSegment(string Text, string Style)
{
    public string[] Values { get; init; } = [];

    /// <summary>The value left out stacks counted while playing.</summary>
    public bool Partial { get; init; }

    /// <summary>A number the game resolves only while playing; <see cref="Text"/> says what stands in for it.</summary>
    public bool Unknown { get; init; }
}

/// <summary>
/// Turns a spell's string table tooltip into segments with its numbers filled in: "@TotalDamage@" is the
/// calculation of that name, "@BaseDamage*100@" a data value times 100, "@Effect1Amount@" an older effect amount,
/// "@spell.EliseR:PassiveTotalDamage@" another spell's, and ".1" before the multiplier the decimals to show. Markup
/// becomes styles and line breaks; icons are dropped.
/// </summary>
public static class SpellTooltip
{
    private static readonly Regex Token = new(
        @"^(?:spell\.(?<spell>[A-Za-z0-9_]+):)?(?<name>[A-Za-z0-9_{}]+)(?:\.(?<precision>\d))?(?:\*(?<scale>-?\d*\.?\d+))?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex Effect = new(@"^Effect(?<index>\d+)Amount$", RegexOptions.CultureInvariant);

    private static readonly Regex Markup = new(
        @"<(?<close>/)?(?<tag>[A-Za-z0-9_]+)[^>]*?/?>|@(?<token>[^@<>]{1,80})@|%i:[A-Za-z0-9_]+%",
        RegexOptions.CultureInvariant
    );

    // Tokens the game fills from the client (hotkeys, modifiers other systems append); they have no number.
    private static readonly HashSet<string> Silent = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpellModifierDescriptionAppend",
        "Hotkey",
        "AbilityResourceName",
    };

    private static readonly Dictionary<string, string> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["magicDamage"] = "magic",
        ["physicalDamage"] = "physical",
        ["trueDamage"] = "true",
        ["healing"] = "heal",
        ["heal"] = "heal",
        ["shield"] = "shield",
        ["speed"] = "speed",
        ["attackSpeed"] = "speed",
        ["slow"] = "speed",
        ["status"] = "key",
        ["keywordMajor"] = "key",
        ["keyword"] = "key",
        ["keywordStealth"] = "key",
        ["keywordName"] = "key",
        ["spellName"] = "key",
        ["attention"] = "key",
    };

    /// <summary>The tooltip with each number given for ranks 1 to <paramref name="ranks"/>; the context's rank is not used.</summary>
    public static TooltipSegment[] Render(string template, SpellContext context, int ranks)
    {
        var segments = new List<TooltipSegment>();
        var styles = new Stack<string>();
        var text = new StringBuilder();
        string Current() => styles.Count > 0 ? styles.Peek() : "";
        void Flush()
        {
            if (text.Length > 0)
            {
                segments.Add(new(WebUtility.HtmlDecode(text.ToString()), Current()));
                text.Clear();
            }
        }

        int at = 0;
        foreach (Match match in Markup.Matches(template))
        {
            text.Append(template, at, match.Index - at);
            at = match.Index + match.Length;
            if (match.Groups["tag"].Success)
            {
                string tag = match.Groups["tag"].Value;
                if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    text.Append('\n');
                }
                else if (tag.Equals("li", StringComparison.OrdinalIgnoreCase) && !match.Groups["close"].Success)
                {
                    text.Append("\n• ");
                }
                else if (Styles.TryGetValue(tag, out var style))
                {
                    Flush();
                    if (match.Groups["close"].Success)
                    {
                        if (styles.Count > 0)
                        {
                            styles.Pop();
                        }
                    }
                    else
                    {
                        styles.Push(style);
                    }
                }
            }
            else if (match.Groups["token"].Success)
            {
                string token = match.Groups["token"].Value;
                if (!Silent.Contains(token))
                {
                    Flush();
                    segments.Add(Value(token, context, ranks, Current()));
                }
            }
        }

        text.Append(template, at, template.Length - at);
        Flush();
        return Merge(segments);
    }

    private static TooltipSegment Value(string token, SpellContext context, int ranks, string style)
    {
        var match = Token.Match(token);
        if (!match.Success)
        {
            return new("?", style) { Unknown = true };
        }

        var spell = match.Groups["spell"].Success ? context.Champion.Spell(match.Groups["spell"].Value) : context.Spell;
        if (spell == null)
        {
            return new("?", style) { Unknown = true };
        }

        double scale = match.Groups["scale"].Success
            ? double.Parse(match.Groups["scale"].Value, CultureInfo.InvariantCulture)
            : 1;
        int? precision = match.Groups["precision"].Success ? match.Groups["precision"].Value[0] - '0' : null;
        string name = match.Groups["name"].Value;
        var numbers = Enumerable
            .Range(1, Math.Max(1, ranks))
            .Select(rank => Resolve(context with { Spell = spell, Rank = rank }, name))
            .ToArray();
        if (numbers.Any(number => number == null))
        {
            return new("?", style) { Unknown = true };
        }

        // A value made only of stacks reads 0 before the game counts them (Garen W's resistances).
        if (numbers.All(number => number!.Value.Partial && number.Value.Value == 0))
        {
            return new("按层数", style) { Unknown = true };
        }

        var values = numbers.Select(number => Format(number!.Value, scale, precision)).ToArray();
        return new("", style)
        {
            Values = values.Distinct().Count() == 1 ? [values[0]] : values,
            Partial = numbers.Any(number => number!.Value.Partial),
        };
    }

    private static SpellNumber? Resolve(SpellContext context, string name)
    {
        if (SpellCalculator.Calculate(context, name) is { } calculated)
        {
            return calculated;
        }

        if (context.Spell.Value(name, context.Rank, context.Mode) is double value)
        {
            return new(value);
        }

        var effect = Effect.Match(name);
        return
            effect.Success
            && context.Spell.Effect(int.Parse(effect.Groups["index"].Value, CultureInfo.InvariantCulture), context.Rank)
                is double amount
            ? new(amount)
            : null;
    }

    /// <summary>
    /// The number as the tooltip shows it: percent calculations ×100 with a sign, then the token's multiplier, to the
    /// decimals the token or the calculation asks for, else fewer decimals the larger the number.
    /// </summary>
    public static string Format(SpellNumber number, double scale = 1, int? precision = null)
    {
        double value = number.Value * scale * (number.Percent ? 100 : 1);
        double size = Math.Abs(value);
        int decimals =
            precision
            ?? number.Precision
            ?? (
                size >= 100 ? 0
                : size >= 10 ? 1
                : 2
            );
        string text = Math.Round(value, Math.Clamp(decimals, 0, 4), MidpointRounding.AwayFromZero)
            .ToString("0.####", CultureInfo.InvariantCulture);
        return (text == "-0" ? "0" : text) + (number.Percent ? "%" : "");
    }

    // Neighbouring text of one style joins, so the widget builds fewer runs.
    private static TooltipSegment[] Merge(List<TooltipSegment> segments)
    {
        var result = new List<TooltipSegment>();
        foreach (var segment in segments)
        {
            if (
                result.Count > 0
                && segment.Values.Length == 0
                && !segment.Unknown
                && result[^1] is { Values.Length: 0, Unknown: false } last
                && last.Style == segment.Style
            )
            {
                result[^1] = last with { Text = last.Text + segment.Text };
            }
            else
            {
                result.Add(segment);
            }
        }

        // Tooltips often end with a line break before the appended modifiers.
        if (result.Count > 0 && result[^1].Values.Length == 0 && !result[^1].Unknown)
        {
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        }

        return result
            .Where(segment => segment.Text.Length > 0 || segment.Values.Length > 0 || segment.Unknown)
            .ToArray();
    }
}
