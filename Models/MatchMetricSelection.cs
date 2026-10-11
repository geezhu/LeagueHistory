namespace League.Models;

public sealed class MatchMetricSelection
{
    public const int Limit = 6;
    private readonly HashSet<string> _keys = new();
    public int Count => _keys.Count;

    public bool Contains(string key) => _keys.Contains(key);

    public bool SetGroup(IEnumerable<string> keys, bool selected)
    {
        var distinct = keys.Distinct().ToArray();
        if (selected && Count == Limit)
        {
            if (distinct.Length > Limit)
            {
                return false;
            }

            _keys.Clear();
        }
        return Set(distinct, selected);
    }

    // A group selection is atomic: do not silently choose only some children.
    public bool Set(IEnumerable<string> keys, bool selected)
    {
        var distinct = keys.Distinct().ToArray();
        if (selected && _keys.Union(distinct).Count() > Limit)
        {
            return false;
        }

        foreach (var key in distinct)
        {
            if (selected)
            {
                _keys.Add(key);
            }
            else
            {
                _keys.Remove(key);
            }
        }
        return true;
    }
}
