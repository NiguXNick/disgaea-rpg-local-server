using System.Reflection;

namespace DrpgServer;

// Read-only view of the client's master tables (from the extracted XDMaster folder), used to
// build server-side records that agree with what the client expects.
public sealed class MasterData
{
    private readonly GameTypes _types;
    private readonly Dictionary<string, Dictionary<ulong, object>> _byId = new();
    private readonly Dictionary<string, List<object>> _rows = new();

    public MasterData(GameTypes types) => _types = types;

    public object? Get(string table, ulong id)
    {
        Load(table);
        return _byId[table].GetValueOrDefault(id);
    }

    public IReadOnlyList<object> All(string table)
    {
        Load(table);
        return _rows[table];
    }

    public static T F<T>(object row, string field)
    {
        for (var t = row.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return (T)Convert.ChangeType(f.GetValue(row)!, typeof(T));
        }
        throw new MissingFieldException(row.GetType().Name, field);
    }

    public static T[] A<T>(object row, string field)
    {
        for (var t = row.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null)
                return f.GetValue(row) is Array arr
                    ? arr.Cast<object>().Select(x => (T)Convert.ChangeType(x, typeof(T))).ToArray()
                    : [];
        }
        throw new MissingFieldException(row.GetType().Name, field);
    }

    // Master dates are "yyyy-MM-dd HH:mm:ss"; the client checks open <= now < close.
    public static bool InTerm(object row, DateTime now)
    {
        var open = F<string>(row, "open_at");
        var close = F<string>(row, "close_at");
        return DateTime.TryParse(open, out var o) && DateTime.TryParse(close, out var c) && o <= now && now < c;
    }

    private void Load(string table)
    {
        lock (_rows)
        {
            if (_rows.ContainsKey(table)) return;
            var tables = _types.MasterTables();
            var rows = tables.TryGetValue(table, out var type)
                ? _types.ReadMasterTable(MasterFix.Dir, table, type)
                : new List<object>();
            _rows[table] = rows;
            _byId[table] = new Dictionary<ulong, object>();
            foreach (var r in rows) _byId[table].TryAdd(F<ulong>(r, "id"), r);
            Log.Info($"Master {table}: {rows.Count} rows.");
        }
    }
}
