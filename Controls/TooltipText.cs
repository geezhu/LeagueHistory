using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Controls;

internal static class TooltipText
{
    internal static string PlainText(string? html)
    {
        var text = Regex.Replace(html ?? "", @"<br\b[^>]*>|</(?:p|div|li)\s*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, @"<[^>]*>", ""));
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00a0', ' ');
        text = Regex.Replace(text, @"[^\S\n]+", " ");
        return Regex.Replace(text, @"\n[ \t]*\n(?:[ \t]*\n)+", "\n\n").Trim();
    }

    public static string Format(string name, string? html)
    {
        var result = new StringBuilder();
        // Native tooltips do not reliably constrain long CJK paragraphs themselves.
        foreach (var paragraph in (name + "\n" + PlainText(html)).Split('\n'))
        {
            var line = new StringBuilder();
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (
                    line.Length > 0
                    && TextRenderer
                        .MeasureText(
                            line.ToString() + element,
                            SystemFonts.StatusFont,
                            Size.Empty,
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                        )
                        .Width > 420
                )
                {
                    result.AppendLine(line.ToString());
                    line.Clear();
                }
                line.Append(element);
            }
            result.AppendLine(line.ToString());
        }
        return result.ToString().TrimEnd();
    }
}
