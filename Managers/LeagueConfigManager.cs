using League.Models;
using Newtonsoft.Json;

namespace League.Managers
{
    public class LeagueConfigManager
    {
        private static readonly LeagueConfigStore Store = new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeagueHistory"),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        );

        public static string ConfigPath => Store.ConfigPath;

        public static LeagueConfig Load()
        {
            return Store.Load();
        }

        public static void Save(LeagueConfig config)
        {
            Store.Save(config);
        }
    }

    /// <summary>Settings survive executable upgrades and changes to the launch directory.</summary>
    public sealed class LeagueConfigStore
    {
        private readonly string[] _legacyPaths;
        private readonly object _gate = new();
        public string ConfigPath { get; }

        public LeagueConfigStore(string settingsDirectory, params string[] legacyDirectories)
        {
            ConfigPath = Path.GetFullPath(Path.Combine(settingsDirectory, "LeagueConfig.json"));
            _legacyPaths = legacyDirectories
                .Select(directory => Path.GetFullPath(Path.Combine(directory, "LeagueConfig.json")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public LeagueConfig Load()
        {
            lock (_gate)
            {
                if (File.Exists(ConfigPath))
                    return Read(File.ReadAllText(ConfigPath));
                foreach (string path in _legacyPaths)
                {
                    if (!File.Exists(path))
                        continue;
                    string json = File.ReadAllText(path);
                    _ = Read(json); // Validate before migrating; leave the original file intact.
                    Write(json, overwrite: false);
                    return Read(File.ReadAllText(ConfigPath));
                }
                return new LeagueConfig();
            }
        }

        public void Save(LeagueConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            lock (_gate)
                Write(JsonConvert.SerializeObject(config, Formatting.Indented), overwrite: true);
        }

        private static LeagueConfig Read(string json) =>
            JsonConvert.DeserializeObject<LeagueConfig>(json)
            ?? throw new JsonSerializationException("配置文件为空，请检查 LeagueConfig.json。");

        private void Write(string json, bool overwrite)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            string temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
                try
                {
                    File.Move(temporary, ConfigPath, overwrite);
                }
                catch (IOException) when (!overwrite && File.Exists(ConfigPath))
                { /* Another instance migrated first. */
                }
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
