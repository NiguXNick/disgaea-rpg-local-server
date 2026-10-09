using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DrpgServer;

// Loads the game's own Assembly-CSharp.dll so responses can be shaped exactly like
// the client's response classes (its MsgPack ObjectPacker rejects unknown keys and
// is strict about integer widths).
public sealed class GameTypes
{
    private readonly Assembly _game;
    private readonly Dictionary<string, Type> _byName = new();

    // RPC method -> type of the "result" field of its response.
    public Dictionary<string, Type> ResultTypes { get; } = new();

    public GameTypes(ServerConfig cfg)
    {
        var managed = Path.Combine(cfg.GameDir, "DISGAEA RPG_Data", "Managed");
        var alc = new ManagedLoadContext(managed);
        _game = alc.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));

        foreach (var t in SafeTypes(_game))
        {
            if (t.FullName != null) _byName.TryAdd(t.FullName.Replace('+', '.'), t);
            _byName.TryAdd(t.Name, t);
            // A few game types (explicit-layout unions) fail to load on modern .NET; skip them.
            try { if (t.DeclaringType != null) _byName.TryAdd(t.DeclaringType.Name + "." + t.Name, t); }
            catch (TypeLoadException) { }
        }

        MapConnectionClasses();
        MapCallSites(Path.Combine(AppContext.BaseDirectory, "methods.tsv"));
        MapPlayerManagerCalls();
        Log.Info($"Game types loaded: {ResultTypes.Count} RPC methods mapped.");
    }

    public Type Get(string name) =>
        _byName.TryGetValue(name, out var t) ? t : throw new KeyNotFoundException($"Game type not found: {name}");

    public Assembly Xd => AssemblyLoadContext.GetLoadContext(_game)!.LoadFromAssemblyName(new AssemblyName("XDDLL"));
    public Assembly Unity => AssemblyLoadContext.GetLoadContext(_game)!.LoadFromAssemblyName(new AssemblyName("UnityEngine.CoreModule"));

    // Master table file prefix (e.g. "MArea") -> record type, from MasterDataManager.TableNameDic
    // plus the Boltrend tables ("BProduct" -> BoltrendProductData) the hotfix adds.
    public Dictionary<string, Type> MasterTables()
    {
        var result = new Dictionary<string, Type>();
        try
        {
            var mgr = Activator.CreateInstance(Get("MasterDataManager"), nonPublic: true)!;
            var dic = (Dictionary<string, string>)mgr.GetType().GetField("TableNameDic")!.GetValue(mgr)!;
            foreach (var (typeName, table) in dic)
                if (_byName.TryGetValue(typeName, out var t)) result[table] = t;
        }
        catch (Exception e) { Log.Warn($"TableNameDic unavailable ({e.GetType().Name}); using derived names."); }

        foreach (var (name, t) in _byName)
        {
            if (name.StartsWith("Boltrend") && name.EndsWith("Data") && !name.Contains('.'))
                result.TryAdd("B" + name["Boltrend".Length..^"Data".Length], t);
            else if (name.StartsWith("Master") && name.EndsWith("Data") && !name.Contains('.'))
                result.TryAdd("M" + name["Master".Length..^"Data".Length], t);
        }
        return result;
    }

    public string DescribeMasterFields(string elementType)
    {
        var rt = Xd.GetType("XD.tool.ReflectionTool")!.GetMethod("GetFieldWithAttribute", BindingFlags.Public | BindingFlags.Static)!;
        var fields = ((System.Collections.IEnumerable)rt.Invoke(null, [Get(elementType), Unity.GetType("UnityEngine.SerializeField")!, true])!)
            .Cast<FieldInfo>();
        return string.Join(" ", fields.Select((f, i) => $"{i}:{f.Name}:{f.FieldType.Name}"));
    }

    // Reads every <table>_N.bin of a master table with the game's own reader (null if unreadable).
    public List<object> ReadMasterTable(string dir, string table, Type elementType)
    {
        var rows = new List<object>();
        _serializeBinary ??= Xd.GetType("XD.Serialize.SerializeBinary")!
            .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(Type), typeof(Type)])!;
        foreach (var file in Directory.GetFiles(dir, table + "_*.bin"))
        {
            var suffix = Path.GetFileNameWithoutExtension(file)[(table.Length + 1)..];
            if (!suffix.All(char.IsDigit)) continue;
            try
            {
                var arr = (Array)_serializeBinary.Invoke(null, [new MemoryStream(File.ReadAllBytes(file)), elementType.MakeArrayType(), Unity.GetType("UnityEngine.SerializeField")!])!;
                rows.AddRange(arr.Cast<object>());
            }
            catch (Exception e) { Log.Warn($"Table {Path.GetFileName(file)} unreadable: {(e.InnerException ?? e).Message}"); }
        }
        return rows;
    }

    private MethodInfo? _serializeBinary;

    public Array? ReadMasterRows(byte[] data, Type elementType)
    {
        _serializeBinary ??= Xd.GetType("XD.Serialize.SerializeBinary")!
            .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(Type), typeof(Type)])!;
        try { return (Array)_serializeBinary.Invoke(null, [new MemoryStream(data), elementType.MakeArrayType(), Unity.GetType("UnityEngine.SerializeField")!])!; }
        catch { return null; }
    }

    // True if the game's own XD.Serialize.SerializeBinary accepts this master table data.
    public bool ReadsMasterBin(byte[] data, Type elementType)
    {
        _serializeBinary ??= Xd.GetType("XD.Serialize.SerializeBinary")!
            .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(Type), typeof(Type)])!;
        try
        {
            _serializeBinary.Invoke(null, [new MemoryStream(data), elementType.MakeArrayType(), Unity.GetType("UnityEngine.SerializeField")!]);
            return true;
        }
        catch { return false; }
    }

    // Diagnostic: run XD.Serialize.SerializeBinary on a master .bin twice, as MasterLoadFile does.
    public string TestMasterBin(string file, string elementType)
    {
        var xd = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "XDDLL")
                 ?? _game.GetReferencedAssemblies().Where(n => n.Name == "XDDLL")
                     .Select(n => AssemblyLoadContext.GetLoadContext(_game)!.LoadFromAssemblyName(n)).First();
        var serializer = xd.GetType("XD.Serialize.SerializeBinary")!;
        var attr = AssemblyLoadContext.GetLoadContext(_game)!.LoadFromAssemblyName(new AssemblyName("UnityEngine.CoreModule"))
            .GetType("UnityEngine.SerializeField")!;
        var serialize = serializer.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(Type), typeof(Type)])!;
        var arrayType = Get(elementType).MakeArrayType();
        var sb = new System.Text.StringBuilder();
        var bytes = File.ReadAllBytes(file);
        for (var i = 1; i <= 2; i++)
        {
            try
            {
                var arr = (Array)serialize.Invoke(null, [new MemoryStream(bytes), arrayType, attr])!;
                sb.AppendLine($"read {i}: OK, {arr.Length} rows");
            }
            catch (Exception e)
            {
                sb.AppendLine($"read {i}: FAILED: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}");
            }
        }
        return sb.ToString();
    }

    // Writes master table rows with the game's own serializer (SerializeBinary.Deserialize is its writer).
    public byte[] WriteMasterBin(Array rows)
    {
        var writer = Xd.GetType("XD.Serialize.SerializeBinary")!
            .GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static, [typeof(object), typeof(Type), typeof(Stream)])!;
        using var ms = new MemoryStream();
        writer.Invoke(null, [rows, Unity.GetType("UnityEngine.SerializeField")!, ms]);
        return ms.ToArray();
    }

    // Instance fields (by wire name) whose type is a nested object, not a value/string/collection.
    public static IEnumerable<string> ObjectFieldNames(Type type)
    {
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var ft = f.FieldType;
                if (ft.IsValueType || ft == typeof(string) || ft.IsArray || (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))) continue;
                var name = f.Name;
                int end;
                if (name[0] == '<' && (end = name.IndexOf('>')) > 1) name = name.Substring(1, end - 1);
                yield return name;
            }
    }

    public Type ListOf(string elementName) => typeof(List<>).MakeGenericType(Get(elementName));
    public Type ArrayOf(string elementName) => Get(elementName).MakeArrayType();

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
    }

    // Every Parameter subclass declares its RPC name in the Method getter and a nested
    // ResponseData whose "result" field gives the payload type.
    private void MapConnectionClasses()
    {
        var parameter = Get("Cloverlab.ConnectionDatas.Parameter");
        foreach (var t in SafeTypes(_game))
        {
            if (t.IsAbstract || !parameter.IsAssignableFrom(t)) continue;
            string? method;
            try
            {
                var obj = RuntimeHelpers.GetUninitializedObject(t);
                method = t.GetProperty("Method")?.GetValue(obj) as string;
            }
            catch { continue; }
            if (string.IsNullOrEmpty(method)) continue;

            var result = ResultField(t);
            if (result != null) ResultTypes.TryAdd(method, result.FieldType);
        }
    }

    // The nested ResponseData may be declared on a base class (ReceiveTrophy : ReceiveTrophyBase),
    // possibly a generic one (ConnectionTrophyIndex : ConnectionTrophyIndexBase<TrophyData>).
    private static FieldInfo? ResultField(Type t)
    {
        for (var b = t; b != null; b = b.BaseType)
        {
            var nested = b.GetNestedType("ResponseData", BindingFlags.Public | BindingFlags.NonPublic);
            if (nested == null) continue;
            if (nested.IsGenericTypeDefinition && b.IsGenericType) nested = nested.MakeGenericType(b.GetGenericArguments());
            var f = nested.GetField("result", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f;
        }
        return null;
    }

    // Generic calls (PlayerConnectionData(method, ...) + GetData<ResponseData<T>>) only reveal
    // their type at the call site; methods.tsv lists those, extracted from the decompiled source.
    private void MapCallSites(string file)
    {
        if (!File.Exists(file)) { Log.Warn($"{file} not found."); return; }
        foreach (var line in File.ReadLines(file))
        {
            if (line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2 || ResultTypes.ContainsKey(parts[0])) continue;
            try
            {
                var t = ResultTypeOf(parts[1].Trim());
                if (t != null) ResultTypes[parts[0]] = t;
            }
            catch (Exception e) { Log.Warn($"methods.tsv: {parts[0]} ({parts[1]}): {e.Message}"); }
        }
    }

    // "ResponseData<X>" / "PlayerManager.ResponseData<X>" -> X; "Foo.ResponseData" -> Foo's result field.
    private Type? ResultTypeOf(string expr)
    {
        expr = expr.Replace("PlayerManager.ResponseData<", "ResponseData<");
        if (expr.StartsWith("ResponseData<") && expr.EndsWith(">"))
            return Parse(expr["ResponseData<".Length..^1]);
        if (expr.EndsWith(".ResponseData"))
            return Get(expr)
                .GetField("result", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType;
        return null;
    }

    private Type Parse(string expr)
    {
        expr = expr.Trim();
        if (expr.EndsWith("[]")) return Parse(expr[..^2]).MakeArrayType();
        if (expr.StartsWith("List<") && expr.EndsWith(">")) return typeof(List<>).MakeGenericType(Parse(expr[5..^1]));
        return expr switch
        {
            "int" => typeof(int), "long" => typeof(long), "ulong" => typeof(ulong), "uint" => typeof(uint),
            "string" => typeof(string), "bool" => typeof(bool), "float" => typeof(float), "double" => typeof(double),
            _ => Get(expr),
        };
    }

    // Calls whose call-site guess is wrong or missing.
    private void MapPlayerManagerCalls()
    {
        void L(string method, string element) => ResultTypes[method] = ListOf(element);

        L("player/characters", "CharacterUserData");
        L("player/weapons", "WeaponUserData");
        L("player/weapon_effects", "WeaponEffectUserData");
        L("player/equipments", "EquipmentUserData");
        L("player/equipment_effects", "EquipmentEffectUserData");
        L("player/items", "PlayerItemData");
        L("player/equipment_decks", "PlayerEquipmentDeckData");
        L("player/clear_stages", "ClearStageData");
        L("player/stage_missions", "PlayerStageMissionData");
        L("player/innocents", "UserInnocentData");

        // TrophyEngine reads these with GetData<PlayerTrophyResponseData<T>>.
        void Trophy(string method, string row) =>
            ResultTypes[method] = Get("Response.PlayerTrophyResponse`1").MakeGenericType(Get(row));
        Trophy("trophy/index", "TrophyData");
        Trophy("trophy/dailies", "TrophyDailyData");
        Trophy("trophy/weeklies", "TrophyWeeklyData");
        Trophy("trophy/repetitions", "TrophyRepetitionData");
    }

    private sealed class ManagedLoadContext(string dir) : AssemblyLoadContext("game", isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            var n = name.Name ?? "";
            // Framework assemblies come from the host runtime; only game/Unity ones from Managed.
            if (n is "mscorlib" or "netstandard" || n.StartsWith("System") || n.StartsWith("Microsoft")) return null;
            var path = Path.Combine(dir, n + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
