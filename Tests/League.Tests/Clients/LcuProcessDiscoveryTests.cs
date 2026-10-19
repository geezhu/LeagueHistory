using League.Clients;

namespace League.Tests.Clients;

/// <summary>
/// The client discovery's command line parser: the port and the token out of the arguments the client was started
/// with, and the reader that hands that command line over in the first place. Every command line here is built by
/// hand, so no test depends on whether a client is running and no real client is touched; the only real process read
/// is the test host's own process. The expectations come from the checks in
/// Tests/StatisticsChecks/LcuProcessDiscoveryChecks.cs, which this class replaces.
/// <para>
/// One member of the old check is not migrated: <c>TryFind</c>, which enumerates the machine's processes for a
/// running <c>LeagueClientUx</c>, reads the winner's command line through WMI and writes the outcome to
/// <c>AppLog.Shared</c>. It takes no input a test can pin, so calling it would make the result depend on whether a
/// client happens to be running on the machine — and on a machine that has one, the test would read the user's real
/// token. <c>TryReadCommandLine(int.MaxValue, …)</c> covers the reader's failure path instead, and the port and token
/// rules are covered through <c>TryParse</c> with hand-built command lines.
/// </para>
/// </summary>
public class LcuProcessDiscoveryTests
{
    private const string ClientArguments =
        "\"C:\\Riot Games\\LeagueClientUx.exe\" \"--app-port=63599\" \"--remoting-auth-token=stub-value\" --no-rads";

    /// <summary>
    /// The value the <c>out</c> parameters are seeded with. A refused parse has to overwrite them, and seeding a
    /// non-empty value is what makes "reports nothing" an assertion about the call instead of about the default a
    /// caller happened to declare; a parameter the call left alone would still hold this text.
    /// </summary>
    private const string Entry = "not-a-connection-parameter";

    [Theory]
    [InlineData(ClientArguments, "63599", "stub-value")]
    [InlineData("--remoting-auth-token=stub-value --app-port=63599", "63599", "stub-value")]
    [InlineData("--app-port=63599 --remoting-auth-token=stub-value --other=1 --no-rads extra", "63599", "stub-value")]
    [InlineData("--app-port=1 --remoting-auth-token=x", "1", "x")]
    [InlineData("--app-port=65535 --remoting-auth-token=x", "65535", "x")]
    // The port is carried as the text the client wrote; it is never re-serialised from the parsed number.
    [InlineData("--app-port=063599 --remoting-auth-token=x", "063599", "x")]
    public void TryParse_ACommandLineThatNamesAClient_ReadsThePortAndTheToken(
        string commandLine,
        string expectedPort,
        string expectedToken
    )
    {
        string port = Entry;
        string token = Entry;

        bool parsed = LcuProcessDiscovery.TryParse(commandLine, out port, out token);

        Assert.True(parsed, "A client's own connection arguments name a client");
        Assert.Equal(expectedPort, port);
        Assert.Equal(expectedToken, token);
    }

    [Theory]
    [InlineData("--app-port=63599 --remoting-auth-token=a%2Fb%3Dc", "a%2Fb%3Dc")]
    [InlineData("--app-port=63599 --remoting-auth-token=a=b", "a=b")]
    [InlineData("\"--app-port=63599\" \"--remoting-auth-token=pass word\"", "pass word")]
    public void TryParse_ATokenWithCharactersThatCouldBeDecoded_KeepsThemVerbatim(
        string commandLine,
        string expectedToken
    )
    {
        string port = Entry;
        string token = Entry;

        bool parsed = LcuProcessDiscovery.TryParse(commandLine, out port, out token);

        Assert.True(parsed, "A token is taken from the argument as it stands");
        Assert.Equal("63599", port);
        Assert.Equal(expectedToken, token);
    }

    [Theory]
    [InlineData(ClientArguments + " --app-port=1234", "1234", "stub-value")]
    [InlineData("--app-port=63599 --remoting-auth-token=first --remoting-auth-token=second", "63599", "second")]
    public void TryParse_ARepeatedArgument_KeepsTheLastOne(
        string commandLine,
        string expectedPort,
        string expectedToken
    )
    {
        string port = Entry;
        string token = Entry;

        bool parsed = LcuProcessDiscovery.TryParse(commandLine, out port, out token);

        Assert.True(parsed, "A repeated argument keeps the last one the client reports");
        Assert.Equal(expectedPort, port);
        Assert.Equal(expectedToken, token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LeagueClientUx.exe --no-rads")]
    [InlineData("--app-port=0 --remoting-auth-token=x")]
    [InlineData("--app-port=-1 --remoting-auth-token=x")]
    [InlineData("--app-port=65536 --remoting-auth-token=x")]
    [InlineData("--app-port=abc --remoting-auth-token=x")]
    [InlineData("--app-port= --remoting-auth-token=x")]
    [InlineData("--app-port=63599")]
    [InlineData("--app-port=63599 --remoting-auth-token=")]
    [InlineData("--app-port=63599 \"--remoting-auth-token=   \"")]
    // The flag is matched whole and case-sensitively, from the start of an argument, with "=" as its separator.
    [InlineData("--app-ports=63599 --remoting-auth-token=x")]
    [InlineData("--app-port:63599 --remoting-auth-token=x")]
    [InlineData("--APP-PORT=63599 --remoting-auth-token=x")]
    [InlineData("x--app-port=63599 --remoting-auth-token=x")]
    public void TryParse_ACommandLineThatNamesNoClient_ReportsNothing(string commandLine)
    {
        string port = Entry;
        string token = Entry;

        bool parsed = LcuProcessDiscovery.TryParse(commandLine, out port, out token);

        Assert.False(parsed, "A command line without a usable port and token names no client");
        Assert.Empty(port);
        Assert.Empty(token);
    }

    [Fact]
    public void TryParse_AMissingCommandLine_NamesNoClient()
    {
        string port = Entry;
        string token = Entry;

        bool parsed = LcuProcessDiscovery.TryParse(null!, out port, out token);

        Assert.False(parsed, "A missing command line names no client");
        Assert.Empty(port);
        Assert.Empty(token);
    }

    /// <summary>
    /// The reader's success path, on the test host's own process: the pid is a parameter and the process is this one,
    /// so the test never reads the state of a client on the machine and never depends on one running.
    /// </summary>
    [Fact]
    public void TryReadCommandLine_OurOwnProcess_ReadsItsCommandLine()
    {
        string commandLine = Entry;
        string error = Entry;

        bool read = LcuProcessDiscovery.TryReadCommandLine(Environment.ProcessId, out commandLine, out error);

        Assert.True(read, $"The reader's own command line can be read: {error}");
        Assert.NotEmpty(commandLine);
        Assert.Empty(error);
    }

    /// <summary>
    /// <c>int.MaxValue</c> is odd, so no Windows process can hold it: the reader never finds a process to read.
    /// </summary>
    [Fact]
    public void TryReadCommandLine_APidThatIsNotRunning_CannotBeRead()
    {
        string commandLine = Entry;
        string error = Entry;

        bool read = LcuProcessDiscovery.TryReadCommandLine(int.MaxValue, out commandLine, out error);

        Assert.False(read, "A process that is not running cannot be read");
        Assert.Empty(commandLine);
        Assert.StartsWith("Win32:", error);
    }
}
