using League.Services;

namespace League.Tests.Services;

/// <summary>
/// What is stripped before a log line or a report leaves the machine.
///
/// Three kinds of thing are handled differently, and the difference is the design. The user's home directory is
/// replaced by a placeholder, because the path shape is still useful and only the name is not. Tokens, URL passwords and
/// bearer headers are deleted outright, because no part of them helps. Player identifiers are replaced by a short
/// salted hash - consistent within one report, so two lines about the same player can still be read together, but
/// meaningless outside it.
/// </summary>
public class DiagnosticRedactorTests
{
    private static DiagnosticRedactor Redactor(string salt = "salt") => new(salt, userProfile: @"C:\Users\someone");

    // ---- nothing to do ----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Redact_Nothing_IsReturnedAsIs(string? text)
    {
        Assert.Equal(text, Redactor().Redact(text!));
    }

    [Fact]
    public void Redact_TextWithoutAnythingSensitive_IsUnchanged()
    {
        const string plain = "对局开始，十名玩家，读取用时 120 毫秒";

        Assert.Equal(plain, Redactor().Redact(plain));
    }

    // ---- the home directory becomes a placeholder ----

    [Fact]
    public void Redact_TheHomeDirectory_BecomesThePlaceholder()
    {
        Assert.Equal(
            @"%USERPROFILE%\Documents\LeagueHistory\settings.json",
            Redactor().Redact(@"C:\Users\someone\Documents\LeagueHistory\settings.json")
        );
    }

    [Fact]
    public void Redact_TheHomeDirectoryWithForwardSlashes_IsReplacedToo()
    {
        // The same path is written both ways depending on who produced it.
        Assert.Equal("%USERPROFILE%/Documents/x", Redactor().Redact("C:/Users/someone/Documents/x"));
    }

    [Fact]
    public void Redact_TheHomeDirectoryIsMatchedWhateverItsCase()
    {
        Assert.StartsWith("%USERPROFILE%", Redactor().Redact(@"c:\users\SOMEONE\x"), StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_AnEmptyHomeDirectory_IsNotSearchedFor()
    {
        // Without a home directory configured there is nothing to replace, and an empty search would corrupt the text.
        DiagnosticRedactor redactor = new("salt", userProfile: "");

        Assert.Equal("abc", redactor.Redact("abc"));
    }

    // ---- tokens are deleted ----

    [Theory]
    [InlineData("--remoting-auth-token=SeCrEt123")]
    [InlineData("--riotclient-auth-token=SeCrEt123")]
    public void Redact_AnAuthTokenArgument_LosesTheToken(string argument)
    {
        string redacted = Redactor().Redact("start " + argument + " --other");

        Assert.DoesNotContain("SeCrEt123", redacted, StringComparison.Ordinal);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
        Assert.Contains("--other", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_AUrlWithCredentials_LosesTheUserAndThePassword()
    {
        // A user name can identify someone as well as a password can, so the whole user-info part goes.
        Assert.Equal("https://***@lcu.example/x", Redactor().Redact("https://user:pass@lcu.example/x"));
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("bearer")]
    [InlineData("Basic")]
    public void Redact_AnAuthorizationHeader_LosesTheCredential(string scheme)
    {
        Assert.Equal($"Authorization: {scheme} ***", Redactor().Redact($"Authorization: {scheme} abcdefgh12345"));
    }

    [Fact]
    public void Redact_AShortWordAfterBearer_IsNotACredential()
    {
        // A word of a few characters is not a token, and eating it would hide ordinary text.
        Assert.Equal("Bearer ok", Redactor().Redact("Bearer ok"));
    }

    [Fact]
    public void Redact_AJsonWebToken_IsDeleted()
    {
        Assert.Equal(
            "token: ***",
            Redactor().Redact("token: eyJhbGciOiJ.eyJzdWIiOiIx.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV")
        );
    }

    // ---- player identifiers become pseudonyms ----

    [Fact]
    public void Redact_AGuid_BecomesAPseudonym()
    {
        string redacted = Redactor().Redact("player 12345678-1234-1234-1234-123456789abc joined");

        Assert.DoesNotContain("12345678-1234-1234-1234-123456789abc", redacted, StringComparison.Ordinal);
        Assert.Contains("id:", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_APuuid_BecomesAPseudonym()
    {
        // A PUUID is seventy-eight characters of base64url; nothing legitimate in a log is that long.
        const string puuid = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-abcd";

        string redacted = Redactor().Redact("puuid: " + puuid);

        Assert.DoesNotContain(puuid, redacted, StringComparison.Ordinal);
        Assert.Contains("id:", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_AShortName_IsNotAPseudonym()
    {
        // Under sixty characters it is ordinary text, and replacing it would make the log unreadable.
        const string name = "abcdefghij";

        Assert.Equal("player " + name + " ok", Redactor().Redact("player " + name + " ok"));
    }

    [Fact]
    public void Redact_TheSameIdentifierTwice_IsTheSamePseudonym()
    {
        // The point of hashing rather than deleting: two lines about one player can still be read together.
        DiagnosticRedactor redactor = Redactor();
        const string guid = "12345678-1234-1234-1234-123456789abc";

        string first = redactor.Redact("a " + guid);
        string second = redactor.Redact("b " + guid);

        Assert.Equal(
            first[(first.IndexOf("id:", StringComparison.Ordinal))..],
            second[(second.IndexOf("id:", StringComparison.Ordinal))..]
        );
    }

    [Fact]
    public void Redact_AnotherSalt_IsAnotherPseudonym()
    {
        // Two reports cannot be joined against each other, because each is salted separately.
        const string guid = "12345678-1234-1234-1234-123456789abc";

        Assert.NotEqual(Redactor("one").Redact(guid), Redactor("two").Redact(guid));
    }

    [Fact]
    public void Redact_TheSameIdentifierInAnotherCase_IsTheSamePseudonym()
    {
        // The identifier is lower-cased first, so the client's casing does not split one player into two.
        DiagnosticRedactor redactor = Redactor();

        Assert.Equal(
            redactor.Redact("12345678-1234-1234-1234-123456789abc"),
            redactor.Redact("12345678-1234-1234-1234-123456789ABC")
        );
    }

    [Fact]
    public void Redact_ThePseudonym_IsAShortHexTag()
    {
        // Eight hex characters is enough to tell players apart within one report and says nothing about them.
        string redacted = Redactor().Redact("12345678-1234-1234-1234-123456789abc");

        Assert.Matches("^id:[0-9A-F]{8}$", redacted);
    }

    [Fact]
    public void Redact_SeveralThingsAtOnce_AreAllHandled()
    {
        // A real line carries all of it at once: a path, a token and a player id.
        string line =
            @"C:\Users\someone\x.log --remoting-auth-token=TOPSECRET player 12345678-1234-1234-1234-123456789abc";

        string redacted = Redactor().Redact(line);

        Assert.DoesNotContain("someone", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("TOPSECRET", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678-1234-1234-1234-123456789abc", redacted, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%", redacted, StringComparison.Ordinal);
        Assert.Contains("id:", redacted, StringComparison.Ordinal);
    }

    // ---- the shared one ----

    [Fact]
    public void Shared_RedactsWithoutBeingConfigured()
    {
        // The application uses this one everywhere; it must work with no arguments.
        Assert.DoesNotContain(
            "eyJhbGciOiJ.eyJzdWIiOiIx.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV",
            DiagnosticRedactor.Shared.Redact("t eyJhbGciOiJ.eyJzdWIiOiIx.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV"),
            StringComparison.Ordinal
        );
    }
}
