using League.Clients;
using static StatisticsCheckSupport;

/// <summary>
/// The client discovery: how the client's own command line is turned into a port and a token, reading a running
/// process's command line, and the discovery itself. Everything here only reads; the token is never printed, and a
/// client that is not running is a normal outcome rather than a failure.
/// </summary>
internal static class LcuProcessDiscoveryChecks
{
    private const string ClientArguments =
        "\"C:\\Riot Games\\LeagueClientUx.exe\" \"--app-port=63599\" \"--remoting-auth-token=stub-value\" --no-rads";

    public static void Run()
    {
        Parsing();
        Reading();
        Discovery();
        Console.WriteLine(
            "PASS: the client discovery parses the port and token out of a command line, reads a running process's "
                + "own command line, and reports a client that is not running without failing."
        );
    }

    private static void Parsing()
    {
        Check(!LcuProcessDiscovery.TryParse(null!, out _, out _), "A missing command line names no client");
        Check(!LcuProcessDiscovery.TryParse("", out _, out _), "An empty command line names no client");
        Check(!LcuProcessDiscovery.TryParse("   ", out _, out _), "A blank command line names no client");
        Check(
            !LcuProcessDiscovery.TryParse("LeagueClientUx.exe --no-rads", out _, out _),
            "A command line without the connection arguments names no client"
        );

        bool parsed = LcuProcessDiscovery.TryParse(ClientArguments, out string port, out string token);
        Check(parsed && port == "63599" && token == "stub-value", "The port and the token are read from the arguments");
        Check(
            LcuProcessDiscovery.TryParse(ClientArguments + " --app-port=1234", out string last, out _)
                && last == "1234",
            "A repeated port keeps the last one the client reports"
        );

        Check(
            !LcuProcessDiscovery.TryParse("--app-port=0 --remoting-auth-token=x", out _, out _),
            "Port zero names no client"
        );
        Check(
            !LcuProcessDiscovery.TryParse("--app-port=70000 --remoting-auth-token=x", out _, out _),
            "A port above the range names no client"
        );
        Check(
            !LcuProcessDiscovery.TryParse("--app-port=63599", out _, out _),
            "A port without a token names no client"
        );
        Check(
            !LcuProcessDiscovery.TryParse("--app-port=  --remoting-auth-token=x", out _, out _),
            "A blank port names no client"
        );
        Check(
            !LcuProcessDiscovery.TryParse("--app-port=abc --remoting-auth-token=x", out _, out _),
            "A port that is not a number names no client"
        );
        Check(
            !LcuProcessDiscovery.TryParse("--app-port=63599 --remoting-auth-token=", out _, out _),
            "A port without a usable token names no client"
        );

        // A refused parse must not leak half of an earlier answer through the out parameters.
        LcuProcessDiscovery.TryParse("--app-port=63599 --remoting-auth-token=x", out string kept, out _);
        LcuProcessDiscovery.TryParse(
            "--app-port=0 --remoting-auth-token=x",
            out string cleared,
            out string clearedToken
        );
        Check(
            kept == "63599" && cleared.Length == 0 && clearedToken.Length == 0,
            "A refused command line reports nothing"
        );
    }

    private static void Reading()
    {
        bool read = LcuProcessDiscovery.TryReadCommandLine(
            Environment.ProcessId,
            out string commandLine,
            out string error
        );
        Check(
            read && commandLine.Length > 0 && error.Length == 0,
            "The check's own command line can be read: " + error
        );

        bool missing = LcuProcessDiscovery.TryReadCommandLine(int.MaxValue, out string none, out string reason);
        Check(!missing && none.Length == 0, "A process that is not running cannot be read");
        Check(
            reason.StartsWith("Win32:", StringComparison.Ordinal)
                || reason.StartsWith("NTSTATUS:", StringComparison.Ordinal),
            "A process that cannot be opened reports why: " + reason
        );
    }

    /// <summary>A client that is not running is the normal case here, so both outcomes are accepted.</summary>
    private static void Discovery()
    {
        bool found = LcuProcessDiscovery.TryFind(out string port, out string token);
        if (found)
        {
            Check(
                int.TryParse(port, out int number) && number is > 0 and <= 65535,
                "A discovered client reports a usable port"
            );
            Check(token.Length > 0, "A discovered client reports a token");
            Check(
                LcuProcessDiscovery.TryParse($"--app-port={port} --remoting-auth-token=x", out string again, out _)
                    && again == port,
                "The discovered port is one the parser accepts"
            );
        }
        else
        {
            Check(port.Length == 0 && token.Length == 0, "A client that was not found reports nothing");
        }
    }
}
