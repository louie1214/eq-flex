namespace EqFlex.Infrastructure.Data;

/// <summary>
/// Loads itemlist.txt into a case-insensitive name→Lucy-item-ID index.
/// Items that share a name (e.g. a classic item later re-used by an expansion) are kept as
/// separate IDs sorted ascending so the oldest (lowest-ID) version comes first.
/// </summary>
public sealed class ItemNameIndex
{
    // Most names have exactly one ID; use int[] to keep per-entry allocation minimal.
    private readonly Dictionary<string, int[]> _nameToIds;

    public int Count => _nameToIds.Count;

    public ItemNameIndex(string dataDir)
    {
        var path = Path.Combine(dataDir, "itemlist.txt");
        // Build with a temporary List<int> per name, then compact to arrays.
        var tmp = new Dictionary<string, List<int>>(140_000, StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            bool header = true;
            foreach (var line in File.ReadLines(path))
            {
                if (header) { header = false; continue; }
                if (line.Length == 0) continue;

                // Format: 1001,"Cloth Cap",https://lucy...
                var commaIdx = line.IndexOf(',');
                if (commaIdx < 1) continue;
                if (!int.TryParse(line.AsSpan(0, commaIdx), out var id)) continue;

                var rest = line.AsSpan(commaIdx + 1);
                string name;
                if (rest.Length > 0 && rest[0] == '"')
                {
                    var closing = rest[1..].IndexOf('"');
                    name = closing < 0 ? new string(rest[1..]) : new string(rest[1..(closing + 1)]);
                }
                else
                {
                    var nextComma = rest.IndexOf(',');
                    name = nextComma < 0 ? new string(rest) : new string(rest[..nextComma]);
                }

                if (name.Length == 0) continue;

                if (!tmp.TryGetValue(name, out var ids))
                {
                    ids = [];
                    tmp[name] = ids;
                }
                if (!ids.Contains(id))
                    ids.Add(id);
            }
        }

        _nameToIds = new Dictionary<string, int[]>(tmp.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, ids) in tmp)
        {
            ids.Sort();                        // ascending → oldest (lowest ID) first
            _nameToIds[name] = [.. ids];
        }
    }

    /// <summary>Returns the primary (lowest/oldest) ID for a name. False if not found.</summary>
    public bool TryGetId(string name, out int id)
    {
        if (_nameToIds.TryGetValue(name, out var ids)) { id = ids[0]; return true; }
        id = 0;
        return false;
    }

    /// <summary>Returns all IDs for a name, sorted ascending (oldest first). False if not found.</summary>
    public bool TryGetIds(string name, out IReadOnlyList<int> ids)
    {
        if (_nameToIds.TryGetValue(name, out var arr)) { ids = arr; return true; }
        ids = [];
        return false;
    }

    /// <summary>True when a name maps to more than one distinct Lucy item ID.</summary>
    public bool HasDuplicates(string name) =>
        _nameToIds.TryGetValue(name, out var ids) && ids.Length > 1;
}
