namespace EqFlex.Infrastructure.Data;

/// <summary>
/// Loads itemlist.txt (134k items) into a case-insensitive name→Lucy-item-ID dictionary at startup.
/// </summary>
public sealed class ItemNameIndex
{
    private readonly Dictionary<string, int> _nameToId;

    public int Count => _nameToId.Count;

    public ItemNameIndex(string dataDir)
    {
        var path = Path.Combine(dataDir, "itemlist.txt");
        _nameToId = new Dictionary<string, int>(140_000, StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path)) return;

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

            if (name.Length > 0)
                _nameToId[name] = id;
        }
    }

    public bool TryGetId(string name, out int id) => _nameToId.TryGetValue(name, out id);
}
