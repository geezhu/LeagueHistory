using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Services;

/// <summary>
/// 写日志和导出报告前去掉敏感信息：用户目录换成 %USERPROFILE%，客户端令牌和授权头删掉，
/// 玩家 ID（PUUID、UUID 形式的 ID）换成同一份报告内一致的短哈希，便于对照又看不出原值。
/// </summary>
public sealed partial class DiagnosticRedactor
{
    private readonly string _salt;
    private readonly string _userProfile;

    public DiagnosticRedactor(string? salt = null, string? userProfile = null)
    {
        _salt = salt ?? Guid.NewGuid().ToString("N");
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static DiagnosticRedactor Shared { get; } = new();

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (_userProfile.Length > 0)
        {
            text = text.Replace(_userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
                .Replace(_userProfile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        text = AuthTokenArgument().Replace(text, "${name}***");
        text = UrlCredentials().Replace(text, "${scheme}***@");
        text = AuthorizationHeader().Replace(text, "${scheme} ***");
        text = JsonWebToken().Replace(text, "***");
        text = Uuid().Replace(text, m => Pseudonym(m.Value));
        return LongIdentifier().Replace(text, m => Pseudonym(m.Value));
    }

    private string Pseudonym(string value) =>
        "id:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_salt + value.ToLowerInvariant())))[..8];

    [GeneratedRegex(
        @"(?<name>--remoting-auth-token=|--riotclient-auth-token=|remoting-auth-token\W{1,3})[^\s""',]+",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex AuthTokenArgument();

    [GeneratedRegex(@"(?<scheme>\b[a-z][a-z0-9+.-]*://[^\s/:@]+:)[^\s/@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"\b(?<scheme>Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex Uuid();

    // PUUIDs are 78 characters of base64url; nothing legitimate in a log is a 60+ character token.
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{60,}(?![A-Za-z0-9_-])")]
    private static partial Regex LongIdentifier();
}
