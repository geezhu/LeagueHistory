using System.Text;

namespace League.Services;

/// <summary>
/// Pinyin initials of Chinese text ("坦克引擎" → "tkyq"), so the Game Bar search works when its overlay keeps the
/// input method in English. Level-1 GB2312 characters are ordered by pinyin, so each initial is a code range; rarer
/// level-2 characters are ordered by radical and are left out. Letters and digits are kept in lower case.
/// </summary>
public static class ChineseInitials
{
    // The first GB2312 code of each initial; i, u and v start no syllable. Level 1 ends at 0xD7F9.
    private static readonly (int Code, char Letter)[] Starts =
    [
        (0xB0A1, 'a'),
        (0xB0C5, 'b'),
        (0xB2C1, 'c'),
        (0xB4EE, 'd'),
        (0xB6EA, 'e'),
        (0xB7A2, 'f'),
        (0xB8C1, 'g'),
        (0xB9FE, 'h'),
        (0xBBF7, 'j'),
        (0xBFA6, 'k'),
        (0xC0AC, 'l'),
        (0xC2E8, 'm'),
        (0xC4C3, 'n'),
        (0xC5B6, 'o'),
        (0xC5BE, 'p'),
        (0xC6DA, 'q'),
        (0xC8BB, 'r'),
        (0xC8F6, 's'),
        (0xCBFA, 't'),
        (0xCDDA, 'w'),
        (0xCEF4, 'x'),
        (0xD1B9, 'y'),
        (0xD4D1, 'z'),
    ];

    private const int LevelOneEnd = 0xD7F9;

    private static readonly Encoding Gb2312 = CreateEncoding();

    private static Encoding CreateEncoding()
    {
        // .NET ships the legacy code pages but only enables them once the provider is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    public static string Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                result.Append(char.ToLowerInvariant(c));
            }
            else if (Initial(c) is char letter)
            {
                result.Append(letter);
            }
        }

        return result.ToString();
    }

    private static char? Initial(char c)
    {
        if (c < 0x4E00 || c > 0x9FFF)
        {
            return null;
        }

        byte[] bytes = Gb2312.GetBytes(c.ToString());
        if (bytes.Length != 2)
        {
            return null;
        }

        int code = bytes[0] << 8 | bytes[1];
        if (code < Starts[0].Code || code > LevelOneEnd)
        {
            return null;
        }

        return Starts.Last(start => start.Code <= code).Letter;
    }
}
