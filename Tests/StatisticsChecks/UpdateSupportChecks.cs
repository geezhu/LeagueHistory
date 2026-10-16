using System.IO.Compression;
using System.Reflection;
using League.Managers;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The local half of the updater: which release asset is the package, how the package is unpacked and how the
/// local version file is read. Nothing here downloads or starts anything.
/// </summary>
internal static class UpdateSupportChecks
{
    public static void Run()
    {
        VersionReading();
        AssetSelection();
        UpdateDirectories();
        PackageExtraction();
        Console.WriteLine(
            "PASS: local version file, release asset selection, update directories and package extraction."
        );
    }

    private static void VersionReading()
    {
        Check(
            VersionInfo.Parse("v1.0.6") == new Version(1, 0, 6)
                && VersionInfo.Parse("1.0.6") == new Version(1, 0, 6)
                && VersionInfo.Parse("V1.0.6") == new Version(1, 0, 6),
            "A version tag is read with or without its v prefix"
        );
        Check(VersionInfo.Parse(" 1.0.6 ") == new Version(1, 0, 6), "Surrounding spaces are ignored");
        Check(VersionInfo.Parse("v1.0") == new Version(1, 0), "A two-part version is accepted");
        Check(
            VersionInfo.Parse("not a version") == new Version(0, 0) && VersionInfo.Parse("") == new Version(0, 0),
            "An unusable tag reads as zero rather than throwing"
        );
        Check(
            VersionInfo.Parse("1.0.10") > VersionInfo.Parse("1.0.9"),
            "Minor versions compare as numbers, so 1.0.10 is newer than 1.0.9"
        );
        Check(VersionInfo.Parse("v2.0.0") > VersionInfo.Parse("1.99.99"), "A higher major version wins");

        string path = Path.Combine(AppContext.BaseDirectory, "version.txt");
        bool existed = File.Exists(path);
        string original = existed ? File.ReadAllText(path) : "";
        try
        {
            File.WriteAllText(path, "1.2.3\r\n");
            Check(
                VersionInfo.GetLocalVersion() == "1.2.3",
                "The local version is the version file without its newline"
            );
        }
        finally
        {
            if (existed)
            {
                File.WriteAllText(path, original);
            }
            else
            {
                File.Delete(path);
            }
        }

        Check(
            VersionInfo.GetLocalVersion() == (existed ? original.Trim() : "0.0.0"),
            "Without a version file the local version falls back to zero"
        );
    }

    private static void AssetSelection()
    {
        Check(
            (string?)
                FindZipAsset.Invoke(
                    null,
                    [
                        JObject.Parse(
                            """{"assets":[{"name":"notes.txt"},{"name":"build.ZIP","browser_download_url":"u"}]}"""
                        ),
                    ]
                ) == "u",
            "The package asset is found whatever the case of its extension"
        );
        Check(
            (string?)
                FindZipAsset.Invoke(
                    null,
                    [
                        JObject.Parse(
                            """{"assets":[{"name":"first.zip","browser_download_url":"one"},{"name":"second.zip","browser_download_url":"two"}]}"""
                        ),
                    ]
                ) == "one",
            "The first package asset wins"
        );
        Check(
            (string?)FindZipAsset.Invoke(null, [JObject.Parse("""{"assets":[{"name":"notes.txt"}]}""")]) == null,
            "A release without a package has no download"
        );
        Check(
            (string?)FindZipAsset.Invoke(null, [JObject.Parse("{}")]) == null,
            "A release without assets has no download"
        );
    }

    private static void UpdateDirectories()
    {
        string root = Temporary("prepare");
        try
        {
            string temp = Path.Combine(root, "temp");
            string extract = Path.Combine(root, "extract");
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "leftover.txt"), "last time");

            PrepareDirectories.Invoke(null, [temp, extract]);
            Check(Directory.Exists(temp) && Directory.Exists(extract), "The update directories are created");
            Check(!File.Exists(Path.Combine(temp, "leftover.txt")), "A leftover temporary directory is cleared first");
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static void PackageExtraction()
    {
        string root = Temporary("extract");
        try
        {
            string extract = Path.Combine(root, "out");
            Directory.CreateDirectory(extract);
            string package = Path.Combine(root, "update.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Write(archive, "readme.txt", "new");
                Write(archive, "sub/nested.txt", "nested");
                Write(archive, "empty/", "");
            }
            File.WriteAllText(Path.Combine(extract, "readme.txt"), "old");

            Extract.Invoke(null, [package, extract]);
            Check(
                File.ReadAllText(Path.Combine(extract, "readme.txt")) == "new",
                "An existing file is replaced by the package"
            );
            Check(
                File.ReadAllText(Path.Combine(extract, "sub", "nested.txt")) == "nested",
                "A file in a subdirectory is created with its directory"
            );
        }
        finally
        {
            Cleanup(root);
        }

        string escapeRoot = Temporary("escape");
        try
        {
            string extract = Path.Combine(escapeRoot, "out");
            Directory.CreateDirectory(extract);
            string package = Path.Combine(escapeRoot, "evil.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Write(archive, "../escaped.txt", "outside");
            }

            bool rejected = false;
            try
            {
                Extract.Invoke(null, [package, extract]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
            {
                rejected = true;
            }

            Check(rejected, "An entry pointing outside the extract directory is refused");
            Check(
                !File.Exists(Path.Combine(escapeRoot, "escaped.txt")),
                "A refused entry writes nothing outside the extract directory"
            );
        }
        finally
        {
            Cleanup(escapeRoot);
        }
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private static string Temporary(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "league-update-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Update check left its temporary folder: " + path);
        }
    }

    private static readonly MethodInfo FindZipAsset = typeof(ConfigUpdateManager).GetMethod(
        "FindZipAsset",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static readonly MethodInfo PrepareDirectories = typeof(ConfigUpdateManager).GetMethod(
        "PrepareUpdateDirectories",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static readonly MethodInfo Extract = typeof(ConfigUpdateManager).GetMethod(
        "ExtractUpdate",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
}
