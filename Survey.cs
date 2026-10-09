using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Fishing Fleet (Survey). Every MSurvey area always has a row (the screen crashes on a missing
// one): idle (no end_at), sailing (end_at in the future) or back (end_at passed). Catches are
// applied directly from survey/end, not through the gift box.
//
// Offline the trips are short: one in-game hour takes MinutesPerHour real minutes. The fleet
// ranks up on its own when its exp reaches the next MSurveyRank (normally a Dark Assembly bill).
public sealed class Survey(MasterData master, Characters chars, Rewards rewards)
{
    private const int MinutesPerHour = 1;
    private const int MaxCondition = 5;
    private const int SurveyExpPerHour = 10;
    private const long CharacterExpPerHour = 100;  // per fish condition level, exp_type 1 areas
    private const int ExpTypeCharacter = 1;
    private const int ResultEmpty = 0, ResultNormal = 1, ResultBig = 2, ResultSuperBig = 3;

    private readonly Random _rng = new();

    public object? Index(Player? p, JsonObject q) =>
        p == null ? null : Obj(("t_surveys", Ensure(p).Select(kv => (object?)Row(kv.Key, kv.Value)).ToList()));

    public object? Start(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var id = U(q, "m_survey_id");
        var s = Ensure(p).GetValueOrDefault(id);
        if (s != null)
        {
            s.Hour = Math.Max(1, I(q, "hour"));
            s.CharacterIds = (q["t_character_ids"] as JsonArray ?? []).Select(x => ulong.TryParse(x?.ToString(), out var v) ? v : 0)
                .Where(x => x != 0 && p.Character(x) != null).ToList();
            s.EndAt = Time.Format(DateTime.UtcNow.AddMinutes(s.Hour * MinutesPerHour));
            Log.Info($"Fishing Fleet: area {id}, {s.Hour}h, {s.CharacterIds.Count} characters, back at {s.EndAt}.");
        }
        return Obj(("t_surveys", Ensure(p).Select(kv => (object?)Row(kv.Key, kv.Value)).ToList())); // never null
    }

    // Cancel and collect. after_t_survey must never be null.
    public object? End(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var id = U(q, "m_survey_id");
        var s = Ensure(p).GetValueOrDefault(id) ?? new SurveyState();
        var crew = s.CharacterIds.Select(p.Character).OfType<OwnedCharacter>().ToList();
        var back = s.EndAt != "" && DateTime.TryParse(s.EndAt, out var end) && end <= DateTime.UtcNow;

        if (B(q, "cancel") || !back)
        {
            Reset(s);
            Log.Info($"Fishing Fleet: area {id} called back.");
            return Obj(("after_t_survey", Row(id, s)), ("result_type", ResultEmpty),
                ("after_t_status", SchemaWriter.Nil), ("after_t_characters", SchemaWriter.Nil),
                ("drop_result", DropResult(p, new List<object?>(), new Rewards.Granted())),
                ("after_t_agenda", SchemaWriter.Nil), ("learning_commands", SchemaWriter.Nil));
        }

        var area = master.Get("MSurvey", id);
        var hours = Math.Max(1, s.Hour);
        // Better fish conditions make bigger catches more likely.
        var roll = _rng.NextDouble() * 10 + s.AreaCondition;
        var resultType = roll >= 13 ? ResultSuperBig : roll >= 9 ? ResultBig : ResultNormal;
        var catches = (hours / 6 + s.AreaCondition) * resultType;

        var drops = new List<object?>();
        var granted = new Rewards.Granted();
        var presents = area == null ? [] : MasterData.Objects(area, "presents");
        for (var i = 0; i < catches && presents.Length > 0; i++)
        {
            var pr = presents[_rng.Next(presents.Length)];
            var type = MasterData.F<int>(pr, "present_type");
            var presentId = MasterData.F<ulong>(pr, "present_id");
            var rarity = MasterData.F<int>(pr, "present_rarity");
            var num = Math.Max(1, MasterData.F<int>(pr, "present_num"));
            if (!rewards.Grant(p, type, presentId, rarity, num, granted)) continue;
            drops.Add(Obj(("id", presentId), ("num", num), ("type", type), ("rank", 0), ("rarity", rarity)));
        }

        if (area != null && MasterData.F<int>(area, "exp_type") == ExpTypeCharacter)
            foreach (var c in crew) chars.AddExp(c, CharacterExpPerHour * hours * s.AreaCondition);
        AddSurveyExp(p, (ulong)(SurveyExpPerHour * hours));

        Log.Info($"Fishing Fleet: area {id} back, result {resultType}, {drops.Count} catches.");
        Reset(s);
        s.AreaCondition = _rng.Next(1, 4);
        return Obj(
            // Characters listed here count as "on a trip" for the client even when idle, so the
            // finished survey goes back empty; the crew comes back in after_t_characters.
            ("after_t_survey", Row(id, s)),
            ("result_type", resultType),
            ("after_t_status", Status(p)),
            ("after_t_characters", crew.Select(c => (object?)Characters.ToWire(c, p.Id)).ToList()),
            ("drop_result", DropResult(p, drops, granted)),
            ("after_t_items", SchemaWriter.Nil), ("after_t_record", SchemaWriter.Nil),
            ("after_t_character_collections", SchemaWriter.Nil), ("failed_in_rebirth_t_character_ids", SchemaWriter.Nil),
            ("t_potential_kinds", SchemaWriter.Nil), ("t_potential_kind_conditions", SchemaWriter.Nil),
            ("after_t_agenda", SchemaWriter.Nil), ("learning_commands", SchemaWriter.Nil));
    }

    // Bribe items raise the fish condition by their effect value, up to 5. Must always succeed
    // (the client has no error handler here and would keep its loading indicator forever).
    public object? Bribe(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var id = U(q, "m_survey_id");
        var s = Ensure(p).GetValueOrDefault(id) ?? new SurveyState();
        var used = new HashSet<ulong>();
        foreach (var b in (q["bribe_data"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var itemId = U(b, "m_item_id");
            var num = (int)Math.Min(I(b, "num"), p.Items.GetValueOrDefault(itemId));
            if (num <= 0) continue;
            var effect = master.Get("MItem", itemId) is { } item ? MasterData.A<int>(item, "effect_value").FirstOrDefault(1) : 1;
            s.AreaCondition = Math.Min(MaxCondition, s.AreaCondition + Math.Max(1, effect) * num);
            p.Items[itemId] -= num;
            used.Add(itemId);
        }
        return Obj(("t_survey", Row(id, s)),
            ("after_t_items", used.Select(i => (object?)Rewards.ItemRow(p, i)).ToList()));
    }

    // MSurveyRank: survey_exp is compared with the next rank's need_exp.
    private void AddSurveyExp(Player p, ulong exp)
    {
        p.SurveyExp += exp;
        p.SurveyExpTotal += exp;
        var needs = master.All("MSurveyRank").ToDictionary(r => MasterData.F<uint>(r, "rank"), r => MasterData.F<ulong>(r, "need_exp"));
        while (needs.TryGetValue(p.SurveyRank + 1, out var need) && p.SurveyExp >= need) p.SurveyRank++;
    }

    // Only survey_exp / survey_exp_total are read from this status.
    private static Dictionary<string, object?> Status(Player p) => Obj(
        ("id", p.Id), ("t_player_id", p.Id), ("survey_rank", p.SurveyRank),
        ("survey_exp", p.SurveyExp), ("survey_exp_total", p.SurveyExpTotal));

    private static Dictionary<string, object?> DropResult(Player p, List<object?> drops, Rewards.Granted g) => Obj(
        ("drop_list", drops), // never null
        ("after_t_item", g.Items.Select(i => (object?)Rewards.ItemRow(p, i)).ToList()),
        ("drop_character", g.Characters),
        ("drop_weapon", g.Weapons.Count > 0 ? Obj(("weapons", g.Weapons), ("weapon_innocents", Obj(("t_innocents", Array.Empty<object>())))) : SchemaWriter.Nil),
        ("drop_equipment", g.Equipment.Count > 0 ? Obj(("equipments", g.Equipment), ("equipment_innocents", Obj(("t_innocents", Array.Empty<object>())))) : SchemaWriter.Nil),
        ("stones", g.Stones ? Rewards.StoneRows(p) : SchemaWriter.Nil));

    private Dictionary<ulong, SurveyState> Ensure(Player p)
    {
        foreach (var area in master.All("MSurvey"))
        {
            var id = MasterData.F<ulong>(area, "id");
            if (!p.Surveys.ContainsKey(id)) p.Surveys[id] = new SurveyState { AreaCondition = _rng.Next(2, 5) };
        }
        return p.Surveys;
    }

    private static void Reset(SurveyState s)
    {
        s.EndAt = "";
        s.Hour = 0;
        s.CharacterIds.Clear();
    }

    private static Dictionary<string, object?> Row(ulong id, SurveyState s) => Obj(
        ("m_survey_id", id),
        ("t_character_ids", s.CharacterIds.Cast<object>().ToArray()), // never null
        ("area_condition", Math.Clamp(s.AreaCondition, 1, MaxCondition)),
        ("hour", s.Hour),
        ("end_at", s.EndAt == "" ? SchemaWriter.Nil : s.EndAt),
        ("m_CharacterUserDataList", SchemaWriter.Nil)); // client cache
}
