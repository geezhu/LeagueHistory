using System.Diagnostics;
using Newtonsoft.Json;

namespace League.Services;

public sealed class PlayerSearchHistory
{
    public const int Limit = 20;
    private readonly string _path;
    private readonly List<string> _items = new();
    public IReadOnlyList<string> Items => _items.AsReadOnly();

    public PlayerSearchHistory(string? path = null)
    {
        _path =
            path
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeagueHistory",
                "search-history.json"
            );
        try
        {
            if (File.Exists(_path))
                _items.AddRange(
                    (JsonConvert.DeserializeObject<string[]>(File.ReadAllText(_path)) ?? Array.Empty<string>())
                        .Where(IsValid)
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(Limit)
                );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"[SearchHistory] Load: {ex.GetType().Name}");
        }
    }

    private static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        var parts = id.Trim().Split('#');
        return parts.Length == 2 && parts.All(p => !string.IsNullOrWhiteSpace(p));
    }

    public void Add(string id)
    {
        if (!IsValid(id))
            return;
        id = id.Trim();
        _items.RemoveAll(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, id);
        if (_items.Count > Limit)
            _items.RemoveRange(Limit, _items.Count - Limit);
        Save();
    }

    public void Remove(string id)
    {
        _items.RemoveAll(s => string.Equals(s, id.Trim(), StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    private void Save()
    {
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonConvert.SerializeObject(_items));
            File.Move(temporary, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[SearchHistory] Save: {ex.GetType().Name}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
