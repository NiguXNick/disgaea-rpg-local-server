using System.Buffers;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using MessagePack;

namespace DrpgServer;

// Serialises loosely-typed data (Dictionary<string, object?> / lists / primitives /
// real game objects) as msgpack shaped exactly like a client type, mirroring the
// client's MsgPack.ObjectPacker:
//  - map keys are field names (auto-property backing fields use the property name)
//  - keys the class lacks are dropped (the client would throw on them)
//  - missing fields are filled from a default instance, so field initialisers apply
//  - List<T> is written as its private fields {_items, _size}, as ObjectPacker sees it
//  - enums, dictionaries, DateTime and interfaces are unsupported by the client and omitted
public static class SchemaWriter
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<Type, (string Name, FieldInfo Field)[]> FieldCache = new();
    private static readonly Dictionary<Type, object?> DefaultCache = new();

    // Explicit msgpack nil, for fields where the client tests "== null" (arrays included).
    public sealed class NilValue { }
    public static readonly NilValue Nil = new();

    private const int MaxDepth = 12;

    public static void Write(ref MessagePackWriter w, Type type, object? data, string path = "", int depth = 0)
    {
        if (depth > MaxDepth && !type.IsPrimitive && type != typeof(string))
        {
            Log.Warn($"Max depth at {path[..Math.Min(path.Length, 300)]}; sending empty.");
            if (type.IsArray) w.WriteArrayHeader(0);
            else if (IsList(type)) { w.WriteMapHeader(2); w.Write("_items"); w.WriteArrayHeader(0); w.Write("_size"); w.Write(0); }
            else w.WriteNil();
            return;
        }
        if (data is NilValue || (data == null && !NeedsValue(type)))
        {
            w.WriteNil();
            return;
        }

        // Null strings go out as "" — the real server always sent strings and the client rarely null-checks.
        if (type == typeof(string)) { w.Write(data == null ? "" : Convert.ToString(data, System.Globalization.CultureInfo.InvariantCulture)); return; }
        if (type == typeof(bool)) { w.Write(data != null && Convert.ToBoolean(data)); return; }
        if (type == typeof(int)) { w.Write(ToInt<int>(data)); return; }
        if (type == typeof(short)) { w.Write(ToInt<short>(data)); return; }
        if (type == typeof(sbyte)) { w.Write(ToInt<sbyte>(data)); return; }
        if (type == typeof(long)) { w.Write(ToInt<long>(data)); return; }
        if (type == typeof(uint)) { w.Write(ToInt<uint>(data)); return; }
        if (type == typeof(ushort)) { w.Write(ToInt<ushort>(data)); return; }
        if (type == typeof(byte)) { w.Write(ToInt<byte>(data)); return; }
        if (type == typeof(char)) { w.Write(ToInt<ushort>(data)); return; }
        if (type == typeof(ulong)) { w.Write(ToInt<ulong>(data)); return; }
        if (type == typeof(float)) { w.Write(data == null ? 0f : Convert.ToSingle(data)); return; }
        if (type == typeof(double)) { w.Write(data == null ? 0d : Convert.ToDouble(data)); return; }

        if (type.IsArray)
        {
            var items = AsList(data);
            var et = type.GetElementType()!;
            w.WriteArrayHeader(items.Count);
            for (var i = 0; i < items.Count; i++) Write(ref w, et, items[i], $"{path}[{i}]", depth + 1);
            return;
        }

        if (IsList(type))
        {
            var items = AsList(data);
            var et = type.GetGenericArguments()[0];
            w.WriteMapHeader(2);
            w.Write("_items");
            w.WriteArrayHeader(items.Count);
            for (var i = 0; i < items.Count; i++) Write(ref w, et, items[i], $"{path}[{i}]", depth + 1);
            w.Write("_size");
            w.Write(items.Count);
            return;
        }

        WriteObject(ref w, type, data, path, depth);
    }

    private static void WriteObject(ref MessagePackWriter w, Type type, object? data, string path, int depth)
    {
        var fields = Fields(type);
        var dict = data as IDictionary<string, object?>;
        var source = dict == null ? data : null;
        var defaults = Default(type);

        if (dict != null)
        {
            foreach (var key in dict.Keys)
                if (!fields.Any(f => f.Name == key))
                    Log.Warn($"Field '{key}' does not exist in {type.Name} ({path}); dropped.");
        }

        var buffer = new ArrayBufferWriter<byte>();
        var inner = new MessagePackWriter(buffer);
        var count = 0;
        foreach (var (name, field) in fields)
        {
            object? value;
            if (dict != null && dict.TryGetValue(name, out var v)) value = v;
            else if (source != null && field.DeclaringType!.IsInstanceOfType(source)) value = field.GetValue(source);
            else
            {
                value = defaults == null ? null : field.GetValue(defaults);
                // Objects created by a class's own constructor can reference each other in cycles;
                // only plain values and collections are taken from defaults.
                if (value != null && !NeedsValue(field.FieldType) && !field.FieldType.IsValueType) value = null;
            }

            inner.Write(name);
            Write(ref inner, field.FieldType, value, path + "." + name, depth + 1);
            count++;
        }
        inner.Flush();
        w.WriteMapHeader(count);
        w.WriteRaw(buffer.WrittenSpan);
    }

    private static bool NeedsValue(Type t) =>
        t.IsPrimitive || t == typeof(string) || t.IsArray || IsList(t);

    private static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

    private static bool Supported(Type t)
    {
        if (t.IsEnum || t.IsInterface || t == typeof(DateTime) || t == typeof(object) || t == typeof(decimal)) return false;
        if (t == typeof(IntPtr) || t == typeof(UIntPtr) || t.IsPointer) return false; // UnityEngine.Object.m_CachedPtr
        if (typeof(Delegate).IsAssignableFrom(t)) return false;
        if (t.IsGenericType && !IsList(t)) return false;
        if (t.IsArray) return Supported(t.GetElementType()!);
        if (IsList(t)) return Supported(t.GetGenericArguments()[0]);
        return true;
    }

    private static (string Name, FieldInfo Field)[] Fields(Type type)
    {
        lock (FieldCache)
        {
            if (FieldCache.TryGetValue(type, out var cached)) return cached;
            var list = new List<(string, FieldInfo)>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    if (!Supported(f.FieldType)) continue;
                    var name = f.Name;
                    int end;
                    if (name[0] == '<' && (end = name.IndexOf('>')) > 1) name = name.Substring(1, end - 1);
                    if (list.Any(x => x.Item1 == name)) continue;
                    list.Add((name, f));
                }
            }
            var arr = list.ToArray();
            FieldCache[type] = arr;
            return arr;
        }
    }

    // Classes whose field initialisers carry real values (app/constants). Other game constructors
    // are not run: some build instances of their own type recursively and overflow the stack.
    private static readonly HashSet<string> RunConstructors = ["SyncDefineData"];

    // Default instance used to fill fields the handler didn't set.
    private static object? Default(Type type)
    {
        lock (DefaultCache)
        {
            if (DefaultCache.TryGetValue(type, out var d)) return d;
            object? obj = null;
            if (!type.IsAbstract && !(type.Assembly.GetName().Name ?? "").StartsWith("UnityEngine"))
            {
                if (RunConstructors.Contains(type.Name))
                {
                    try { obj = Activator.CreateInstance(type, nonPublic: true); }
                    catch { }
                }
                if (obj == null)
                {
                    try { obj = RuntimeHelpers.GetUninitializedObject(type); } catch { }
                }
            }
            DefaultCache[type] = obj;
            return obj;
        }
    }

    private static IList AsList(object? data) => data switch
    {
        null => Array.Empty<object>(),
        IList l => l,
        IEnumerable e when data is not string => e.Cast<object?>().ToList(),
        _ => new[] { data },
    };

    private static T ToInt<T>(object? data) where T : struct
    {
        if (data == null) return default;
        if (data is bool b) data = b ? 1 : 0;
        return (T)Convert.ChangeType(data, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
