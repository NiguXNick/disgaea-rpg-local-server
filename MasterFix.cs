using System.Reflection;
using System.Text;

namespace DrpgServer;

// The master data shipped in StreamingAssets/Data/master is about a year older than the
// 3.2.10 client: tables gained (and occasionally lost) fields since. The live server replaced it
// via master_bin; without it XD.Serialize.SerializeBinary hits end-of-stream, MasterLoadFile.LoadAll
// dies and the title screen waits forever.
//
// The format has no field metadata (int32 count, then each record's [SerializeField] fields in
// ReflectionTool.GetFieldWithAttribute order, "#EOF" at the end). For each table the game's own
// reader rejects, we search for the smallest set of edits (fields missing from the old file and
// obsolete fields present only in it) that makes the file parse exactly, then rewrite it in the
// current layout (defaults for new fields, obsolete ones dropped). Originals are kept as *.bak.
public sealed class MasterFix
{
    private const int MaxEdits = 3;
    private const long MaxAttemptsPerLevel = 400_000;
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);
    private static readonly Type[] ExtraKinds = [typeof(int), typeof(long), typeof(bool), typeof(string), typeof(int[]), typeof(ulong[])];

    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Boltrend", "DISGAEA RPG", "Boltrend", "XDMaster");

    private readonly GameTypes _types;
    private readonly Dictionary<string, Type> _tables;
    private readonly MethodInfo _fieldsWithAttribute;
    private readonly Type _serializeField;
    private readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();

    public MasterFix(GameTypes types)
    {
        _types = types;
        _tables = types.MasterTables();
        _fieldsWithAttribute = types.Xd.GetType("XD.tool.ReflectionTool")!
            .GetMethod("GetFieldWithAttribute", BindingFlags.Public | BindingFlags.Static)!;
        _serializeField = types.Unity.GetType("UnityEngine.SerializeField")!;
    }

    // Old layouts worked out by hand from the bytes, where the search is ambiguous or too slow:
    // the fields listed did not exist yet in the shipped files.
    private static readonly Dictionary<string, string[]> KnownMissing = new()
    {
        ["MStage"] = ["appear_at", "auto_flg", "party_check_flg", "defense_point", "display_info_flg"],
        ["MCommand"] = ["effect_values_sub2", "trigger_types", "trigger_type_params", "trigger_targets"],
        ["MEnemy"] = ["m_character_retrofit_ids", "weapons", "equipments", "weapon_effects", "equipment_effects",
            "innocents", "t_potential_kinds",
            "weapon_mastery_rank_1", "weapon_mastery_exp_1", "weapon_mastery_rank_2", "weapon_mastery_exp_2",
            "weapon_mastery_rank_3", "weapon_mastery_exp_3", "weapon_mastery_rank_4", "weapon_mastery_exp_4",
            "weapon_mastery_rank_5", "weapon_mastery_exp_5", "weapon_mastery_rank_6", "weapon_mastery_exp_6",
            "weapon_mastery_rank_7", "weapon_mastery_exp_7", "weapon_mastery_rank_8", "weapon_mastery_exp_8",
            "weapon_mastery_rank_9", "weapon_mastery_exp_9",
            "mana_rank_hp", "mana_rank_atk", "mana_rank_def", "mana_rank_inte", "mana_rank_res", "mana_rank_spd"],
    };

    public void Apply()
    {
        if (!Directory.Exists(Dir)) return;
        lock (this)
        {
            int fixedCount = 0, failed = 0;
            var byTable = Directory.GetFiles(Dir, "*.bin")
                .GroupBy(f => { var n = Path.GetFileNameWithoutExtension(f); return n[..n.LastIndexOf('_')]; });
            foreach (var group in byTable)
            {
                if (!_tables.TryGetValue(group.Key, out var type)) continue;
                try
                {
                    var (ok, bad) = FixTable(group.Key, group.ToArray(), type);
                    fixedCount += ok;
                    failed += bad;
                }
                catch (Exception e)
                {
                    failed++;
                    Log.Error($"Master {group.Key}: {e.Message}");
                }
            }
            if (fixedCount > 0 || failed > 0)
                Log.Info($"Master data: {fixedCount} files upgraded, {failed} could not be fixed automatically.");
            AddMissingTables();
        }
    }

    // Tables the live server's master had but the shipped one lacks entirely.
    // MBingoGroup: with no bingo group in term, BingoController.SetUpAndLoginPlay calls its
    // completion callback before the bingo object exists and crashes, which stops the home
    // screen's post-login popup chain under a dimmed background. One always-open group (with the
    // server answering bingo/index as "already drawn today") lets the chain finish.
    private void AddMissingTables()
    {
        var flist = Path.Combine(Dir, "flist");
        if (!File.Exists(flist)) return;
        if (!_tables.TryGetValue("MBingoGroup", out var bingoType)) return;

        var file = Path.Combine(Dir, "MBingoGroup_1.bin");
        if (!File.Exists(file))
        {
            var row = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(bingoType);
            void Set(string name, object value)
            {
                for (var t = bingoType; t != null; t = t.BaseType)
                {
                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (f != null) { f.SetValue(row, value); return; }
                }
            }
            Set("id", 1UL);
            Set("rotation_type", 1);
            Set("cell_number", 9);
            Set("m_product_id", 0UL);
            Set("open_at", "2020-01-01 00:00:00");
            Set("close_at", "2099-12-31 23:59:59");
            var rows = Array.CreateInstance(bingoType, 1);
            rows.SetValue(row, 0);
            var bytes = _types.WriteMasterBin(rows);
            if (!_types.ReadsMasterBin(bytes, bingoType))
            {
                Log.Warn("Could not build MBingoGroup_1.bin.");
                return;
            }
            File.WriteAllBytes(file, bytes);
            Log.Info("Master data: added an always-open bingo group (MBingoGroup_1.bin).");
        }

        var lines = File.ReadAllLines(flist).ToList();
        if (!lines.Contains("MBingoGroup_1.bin"))
        {
            if (!File.Exists(flist + ".bak")) File.Copy(flist, flist + ".bak"); // restore-game.ps1 puts it back
            lines.Add("MBingoGroup_1.bin");
            File.WriteAllLines(flist, lines);
        }

        PaidQuartzBannersAcceptFreeQuartz();
        Rewrite("MStage", "act", v => v is int a && a != 0 ? 0 : null, "stages no longer cost AP");
    }

    // Generic offline tweak: change one field of every row of a table (change returns null to keep).
    private void Rewrite(string table, string fieldName, Func<object?, object?> change, string what)
    {
        if (!_tables.TryGetValue(table, out var type)) return;
        var field = Fields(type).FirstOrDefault(f => f.Name == fieldName);
        if (field == null) return;
        foreach (var file in Directory.GetFiles(Dir, table + "_*.bin"))
        {
            var rows = _types.ReadMasterRows(File.ReadAllBytes(file), type);
            if (rows == null) continue;
            var changed = 0;
            foreach (var row in rows)
            {
                var updated = change(field.GetValue(row));
                if (updated == null) continue;
                field.SetValue(row, updated);
                changed++;
            }
            if (changed == 0) continue;
            var bytes = _types.WriteMasterBin(rows);
            if (!_types.ReadsMasterBin(bytes, type)) continue;
            if (!File.Exists(file + ".bak")) File.Copy(file, file + ".bak");
            File.WriteAllBytes(file, bytes);
            Log.Info($"Master {Path.GetFileName(file)}: {changed} rows, {what}.");
        }
    }

    // Offline there's no real money: banners priced in paid-only quartz (MGacha.price_type 3)
    // become regular quartz banners (price_type 2), payable with free quartz.
    private void PaidQuartzBannersAcceptFreeQuartz()
    {
        if (!_tables.TryGetValue("MGacha", out var gachaType)) return;
        var fields = Fields(gachaType);
        var priceType = fields.FirstOrDefault(f => f.Name == "price_type");
        var gachaTypeField = fields.FirstOrDefault(f => f.Name == "gacha_type");
        var closeAt = fields.FirstOrDefault(f => f.Name == "close_at");
        if (priceType == null || gachaTypeField == null || closeAt == null) return;
        const string Forever = "2099-12-31 23:59:59";
        foreach (var file in Directory.GetFiles(Dir, "MGacha_*.bin"))
        {
            var rows = _types.ReadMasterRows(File.ReadAllBytes(file), gachaType);
            if (rows == null) continue;
            int paid = 0, reopened = 0;
            foreach (var row in rows)
            {
                if ((int)priceType.GetValue(row)! == 3)
                {
                    priceType.SetValue(row, 2);
                    paid++;
                }
                // Expired banners come back (tutorial gachas stay as they are).
                if ((int)gachaTypeField.GetValue(row)! != 2 && (string?)closeAt.GetValue(row) != Forever)
                {
                    closeAt.SetValue(row, Forever);
                    reopened++;
                }
            }
            if (paid == 0 && reopened == 0) continue;
            var bytes = _types.WriteMasterBin(rows);
            if (!_types.ReadsMasterBin(bytes, gachaType)) continue;
            if (!File.Exists(file + ".bak")) File.Copy(file, file + ".bak");
            File.WriteAllBytes(file, bytes);
            Log.Info($"Master {Path.GetFileName(file)}: {paid} paid-quartz banners now accept free quartz, {reopened} expired banners reopened.");
        }
    }

    // Diagnostic: fix only one table, with timing.
    public void ApplyTable(string table)
    {
        if (!_tables.TryGetValue(table, out var type)) { Log.Error($"Unknown table: {table}"); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (ok, bad) = FixTable(table, Directory.GetFiles(Dir, table + "_*.bin"), type);
        Log.Info($"{table}: {ok} fixed, {bad} failed in {sw.Elapsed.TotalSeconds:f1}s");
    }

    private readonly record struct Slot(int Field, Type Type); // Field = -1 for obsolete data

    // All files of a table come from the same export, so one old layout must explain all of them.
    private (int Fixed, int Failed) FixTable(string table, string[] files, Type type)
    {
        var broken = files.Select(f => (File: f, Data: File.ReadAllBytes(f)))
            .Where(x => !_types.ReadsMasterBin(x.Data, type)).ToList();
        if (broken.Count == 0) return (0, 0);

        var fields = Fields(type);
        if (!fields.All(f => Supported(f.FieldType, 0)))
        {
            Log.Warn($"Master {table}: unsupported field type; skipped.");
            return (0, broken.Count);
        }
        var types = fields.Select(f => f.FieldType).ToArray();
        _names = fields.Select(f => f.Name).ToArray();

        IEnumerable<Slot[]> candidates;
        if (KnownMissing.TryGetValue(table, out var knownNames))
        {
            var missing = knownNames.Select(n => Array.IndexOf(_names, n)).ToArray();
            if (missing.Any(i => i < 0)) { Log.Warn($"Master {table}: known layout does not match the class."); return (0, broken.Count); }
            // The hand-made layout, then the same with one field more or one field less missing.
            var variants = new List<int[]> { missing };
            variants.AddRange(Enumerable.Range(0, types.Length).Where(i => !missing.Contains(i)).Select(i => missing.Append(i).ToArray()));
            variants.AddRange(missing.Select(j => missing.Where(i => i != j).ToArray()));
            candidates = variants.Select(m => Layouts(types, m, 0).First());
        }
        else
        {
            // Most likely first: fields appended at the end of the class (a pure tail), then one
            // edit anywhere, then a tail plus up to two in the middle, then 2-3 arbitrary edits.
            // Order matters when sizes coincide: MGachaGroupItem lacks max_level+level (2 ints at
            // the end), which a single missing ulong item_id in the middle would also fit.
            candidates = TailCandidates(types, maxInner: 0).Concat(Candidates(types, 1, 1))
                .Concat(TailCandidates(types, maxInner: 2)).Concat(Candidates(types, 2, MaxEdits));
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var layout in candidates)
        {
            if (clock.Elapsed > TimeLimit) break;
            var rewritten = new List<byte[]>();
            foreach (var (_, data) in broken)
            {
                var spans = Parse(data, layout);
                if (spans == null) break;
                var r = Rewrite(data, types, layout, spans);
                if (!_types.ReadsMasterBin(r, type)) break;
                rewritten.Add(r);
            }
            if (rewritten.Count != broken.Count) continue;

            for (var i = 0; i < broken.Count; i++)
            {
                if (!File.Exists(broken[i].File + ".bak")) File.WriteAllBytes(broken[i].File + ".bak", broken[i].Data);
                File.WriteAllBytes(broken[i].File, rewritten[i]);
            }
            var added = Enumerable.Range(0, types.Length).Where(f => !layout.Any(s => s.Field == f)).Select(f => fields[f].Name);
            var removed = layout.Where(s => s.Field < 0).Select(s => s.Type.Name);
            Log.Info($"Master {table} ({broken.Count} files): +[{string.Join(", ", added)}] -[{string.Join(", ", removed)}]");
            return (broken.Count, 0);
        }

        Log.Warn($"Master {table} ({type.Name}): could not work out the old layout.");
        return (0, broken.Count);
    }

    private static IEnumerable<Slot[]> Candidates(Type[] types, int minEdits, int maxEdits)
    {
        for (var edits = minEdits; edits <= maxEdits; edits++)
        {
            long attempts = 0;
            // Prefer explanations with more missing fields (additions are far more common than removals).
            for (var extras = 0; extras <= edits; extras++)
            {
                var missingCount = edits - extras;
                if (missingCount >= types.Length) continue;
                foreach (var missing in Combinations(types.Length, missingCount))
                    foreach (var layout in Layouts(types, missing, extras))
                    {
                        if (++attempts > MaxAttemptsPerLevel) goto nextLevel;
                        yield return layout;
                    }
            }
            nextLevel:;
        }
    }

    private static IEnumerable<Slot[]> TailCandidates(Type[] types, int maxInner)
    {
        for (var tail = 1; tail < types.Length; tail++)
        {
            var tailFields = Enumerable.Range(types.Length - tail, tail).ToArray();
            for (var k = 0; k <= maxInner; k++)
                foreach (var inner in Combinations(types.Length - tail, k))
                    yield return Layouts(types, inner.Concat(tailFields).ToArray(), 0).First();
        }
    }

    // Current fields minus `missing`, with `extras` obsolete slots of any kind inserted anywhere.
    private static IEnumerable<Slot[]> Layouts(Type[] types, int[] missing, int extras)
    {
        var present = Enumerable.Range(0, types.Length).Where(i => Array.IndexOf(missing, i) < 0)
            .Select(i => new Slot(i, types[i])).ToList();
        if (extras == 0)
        {
            yield return present.ToArray();
            yield break;
        }
        foreach (var positions in Multisets(present.Count + 1, extras))
        {
            foreach (var kinds in Products(ExtraKinds.Length, extras))
            {
                var layout = new List<Slot>(present);
                // Insert from the back so earlier positions stay valid.
                for (var e = extras - 1; e >= 0; e--)
                    layout.Insert(positions[e], new Slot(-1, ExtraKinds[kinds[e]]));
                yield return layout.ToArray();
            }
        }
    }

    // Several layouts can fit the bytes when neighbouring fields share a type (MStage: a missing
    // appear_at vs a missing boss_bgm). String fields whose names imply a format break the tie.
    private static bool Plausible(string field, string value)
    {
        if (value is "null_s" or "") return true;
        if (field.EndsWith("_at") || field.EndsWith("_date"))
            return value.Length >= 8 && char.IsDigit(value[0]) && value.Contains('-');
        if (field.Contains("bgm")) return value.StartsWith("bgm", StringComparison.OrdinalIgnoreCase) || !value.Contains(' ');
        if (field.Contains("resource") || field.EndsWith("_name") && field != "nickname" && field.Contains("res"))
            return !value.Contains(' ');
        return true;
    }

    private string[] _names = [];

    // Per record: byte span of each slot; null if the layout doesn't fit the file exactly.
    private List<(int Start, int End)[]>? Parse(byte[] data, Slot[] layout)
    {
        var r = new Reader(data, this);
        try
        {
            var n = r.Int32();
            if (n < 0 || n > 1_000_000) return null;
            var records = new List<(int, int)[]>(n);
            for (var i = 0; i < n; i++)
            {
                var spans = new (int, int)[layout.Length];
                for (var s = 0; s < layout.Length; s++)
                {
                    var start = r.Pos;
                    if (layout[s].Type == typeof(string) && layout[s].Field >= 0)
                    {
                        if (!r.TryString(out var str) || !Plausible(_names[layout[s].Field], str)) return null;
                    }
                    else if (!r.Skip(layout[s].Type, 0)) return null;
                    spans[s] = (start, r.Pos);
                }
                records.Add(spans);
            }
            if (r.Pos == data.Length) return records;
            return r.TryString(out var eof) && eof == "#EOF" && r.Pos == data.Length ? records : null;
        }
        catch (ArgumentException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    private byte[] Rewrite(byte[] data, Type[] types, Slot[] layout, List<(int Start, int End)[]> records)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(records.Count);
        foreach (var spans in records)
        {
            var lastIntArrayLength = 0;
            for (var f = 0; f < types.Length; f++)
            {
                var s = Array.FindIndex(layout, x => x.Field == f);
                if (s >= 0)
                {
                    w.Write(data, spans[s].Start, spans[s].End - spans[s].Start);
                    if (types[f] == typeof(int[])) lastIntArrayLength = Math.Max(0, BitConverter.ToInt32(data, spans[s].Start));
                }
                else if (types[f] == typeof(int[]))
                {
                    // New int[] fields run parallel to their neighbours (e.g. MCommand.trigger_types
                    // per effect), so give them the same length, zero-filled.
                    w.Write(lastIntArrayLength);
                    for (var i = 0; i < lastIntArrayLength; i++) w.Write(0);
                }
                else WriteDefault(w, types[f]);
            }
        }
        w.Write("#EOF");
        w.Flush();
        return ms.ToArray();
    }

    private void WriteDefault(BinaryWriter w, Type t)
    {
        if (t.IsArray) { w.Write(0); return; }
        if (t == typeof(string)) { w.Write("null_s"); return; }
        if (t == typeof(bool)) { w.Write(false); return; }
        if (t == typeof(byte)) { w.Write((byte)0); return; }
        if (t == typeof(short) || t == typeof(ushort)) { w.Write((short)0); return; }
        if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) { w.Write(0); return; }
        if (t == typeof(long) || t == typeof(ulong) || t == typeof(double)) { w.Write(0L); return; }
        foreach (var f in Fields(t)) WriteDefault(w, f.FieldType); // nested object
    }

    private FieldInfo[] Fields(Type t)
    {
        if (_fieldCache.TryGetValue(t, out var f)) return f;
        f = ((System.Collections.IEnumerable)_fieldsWithAttribute.Invoke(null, [t, _serializeField, true])!)
            .Cast<FieldInfo>().ToArray();
        _fieldCache[t] = f;
        return f;
    }

    private bool Supported(Type t, int depth)
    {
        if (depth > 4) return false;
        if (t.IsArray) return Supported(t.GetElementType()!, depth + 1);
        if (IsScalar(t)) return true;
        return t.IsClass && Fields(t).All(f => Supported(f.FieldType, depth + 1));
    }

    private static bool IsScalar(Type t) =>
        t == typeof(string) || t == typeof(bool) || t == typeof(byte) || t == typeof(short) || t == typeof(ushort) ||
        t == typeof(int) || t == typeof(uint) || t == typeof(float) || t == typeof(long) || t == typeof(ulong) || t == typeof(double);

    // k-subsets of [0, n), latest indices first (new fields are usually appended to a class).
    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        if (k == 0) { yield return []; yield break; }
        var idx = Enumerable.Range(n - k, k).ToArray();
        while (true)
        {
            yield return (int[])idx.Clone();
            var i = 0;
            while (i < k && idx[i] == i) i++;
            if (i == k) yield break;
            idx[i]--;
            for (var j = i - 1; j >= 0; j--) idx[j] = idx[j + 1] - 1;
        }
    }

    // Non-decreasing k-tuples over [min, n), latest first.
    private static IEnumerable<int[]> Multisets(int n, int k, int min = 0)
    {
        if (k == 0) { yield return []; yield break; }
        for (var v = n - 1; v >= min; v--)
            foreach (var rest in Multisets(n, k - 1, v))
                yield return [v, .. rest];
    }

    private static IEnumerable<int[]> Products(int n, int k)
    {
        var idx = new int[k];
        while (true)
        {
            yield return (int[])idx.Clone();
            var i = k - 1;
            while (i >= 0 && idx[i] == n - 1) { idx[i] = 0; i--; }
            if (i < 0) yield break;
            idx[i]++;
        }
    }

    // Byte reader that rejects implausible values early so wrong layouts fail fast.
    private sealed class Reader(byte[] data, MasterFix owner)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);
        public int Pos;

        public int Int32()
        {
            if (Pos + 4 > data.Length) throw new IndexOutOfRangeException();
            var v = BitConverter.ToInt32(data, Pos);
            Pos += 4;
            return v;
        }

        public bool Skip(Type t, int depth)
        {
            if (t.IsArray)
            {
                if (Pos + 4 > data.Length) return false;
                var n = Int32();
                if (n < -1 || n > 100_000) return false;
                for (var i = 0; i < n; i++) if (!Skip(t.GetElementType()!, depth + 1)) return false;
                return true;
            }
            if (t == typeof(string)) return TryString(out _);
            if (t == typeof(bool))
            {
                if (Pos >= data.Length || data[Pos] > 1) return false;
                Pos++;
                return true;
            }
            if (IsScalar(t))
            {
                var size = t == typeof(byte) ? 1 : t == typeof(short) || t == typeof(ushort) ? 2
                    : t == typeof(long) || t == typeof(ulong) || t == typeof(double) ? 8 : 4;
                if (Pos + size > data.Length) return false;
                Pos += size;
                return true;
            }
            foreach (var f in owner.Fields(t)) if (!Skip(f.FieldType, depth + 1)) return false;
            return true;
        }

        public bool TryString(out string s)
        {
            s = "";
            int len = 0, shift = 0;
            while (true)
            {
                if (Pos >= data.Length || shift > 28) return false;
                var b = data[Pos++];
                len |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            if (len < 0 || len > 200_000 || Pos + len > data.Length) return false;
            try { s = StrictUtf8.GetString(data, Pos, len); }
            catch (DecoderFallbackException) { return false; }
            Pos += len;
            return true;
        }
    }
}
