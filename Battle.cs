using System.Text;
using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Story stages: stage select data, battle/start, battle/end, battle/story and items.
//
// Offline everything is unlocked: the client decides what is selectable purely from its t_stages
// rows (a stage is open when the stage in its MStage.appear_m_stage_id is cleared), so every stage
// is reported as cleared. The player's real progress (clear counts, mission stars) is kept
// separately and drives first-clear rewards.
public sealed partial class Battle(MasterData master, Characters chars, Func<Player, Dictionary<string, object?>> status, GameTypes types, Shop shop)
{
    private const int EpisodeTypeMain = 1;      // SyncDefineData.episode_type_main
    private const int BattleTypeNormal = 1;     // battle_type_normal
    private const int BattleResultWin = 1;      // battle_result_win
    private const ulong ItemIdHl = 101;         // item_id_point
    private const int PresentTypeItem = 1;      // present_type_item
    // Quartz is earned on every win, more on harder stages: 5 per difficulty rank (MStage.rank:
    // 1 easy, 2 normal, 3 hard…) plus a tenth of the stage's exp, which grows along the story.
    // First clears and new mission stars add a bonus.
    private const int QuartzPerRank = 5;
    private const int StageExpPerQuartz = 10;
    private const int FirstClearQuartz = 50;
    private const int MissionStarQuartz = 10;
    // Offline HL is boosted so the shop is usable at the original prices.
    private const int HlMultiplier = 5;
    private const int PresentTypeCharacter = 2, PresentTypeWeapon = 3, PresentTypeEquipment = 4;
    private const double DropChance = 0.3;

    private readonly Random _rng = new();
    private static string Now => Time.Format(DateTime.UtcNow);

    // ---- Stage select -----------------------------------------------------------------------

    // t_stages / t_stage_missions are fetched at login only when player/sync lists them; an empty
    // updated_at forces a full resync.
    public object? Sync(Player? p, JsonObject q) => new List<object?>
    {
        Obj(("name", "t_stages"), ("updated_at", "")),
        Obj(("name", "t_stage_missions"), ("updated_at", "")),
    };

    // Every stage in master, as cleared (one page: no X-IS-LAST-PAGE header means last page).
    public object? ClearStages(Player? p, JsonObject q)
    {
        if (p == null) return null;
        if (I(q, "page", 1) > 1) return new List<object?>();
        return master.All("MStage").Select(s => (object?)ClearStageRow(p, s)).ToList();
    }

    private Dictionary<string, object?> ClearStageRow(Player p, object stage)
    {
        var id = MasterData.F<ulong>(stage, "id");
        return Obj(
            ("id", id), ("t_player_id", p.Id), ("rank", MasterData.F<int>(stage, "rank")), ("m_stage_id", id),
            ("clear_num", Math.Max(1, p.StageClears.GetValueOrDefault(id))), ("clear_flg", true),
            ("lose_num", p.StageLosses.GetValueOrDefault(id)), ("del_flg", false),
            ("created_at", p.CreatedAt), ("updated_at", Now));
    }

    public object? StageMissions(Player? p, JsonObject q)
    {
        if (p == null) return null;
        if (I(q, "page", 1) > 1) return new List<object?>();
        return p.StageMissions.Select(kv => (object?)Obj(
            ("id", kv.Key), ("t_player_id", p.Id), ("m_stage_id", kv.Key),
            ("rank", master.Get("MStage", kv.Key) is { } s ? MasterData.F<int>(s, "rank") : 1),
            ("clear_flg_1", kv.Value[0]), ("clear_flg_2", kv.Value[1]), ("clear_flg_3", kv.Value[2]),
            ("created_at", p.CreatedAt), ("updated_at", Now))).ToList();
    }

    // One row per episode_type.
    public object? StageCurrents(Player? p, JsonObject q) =>
        p == null ? null : new List<object?> { StageCurrent(p, p.CurrentStage) };

    private Dictionary<string, object?> StageCurrent(Player p, ulong stageId) => Obj(
        ("id", 1UL), ("t_player_id", p.Id), ("episode_type", EpisodeTypeMain),
        ("current_id", stageId != 0 ? stageId : FirstStageId()),
        ("created_at", p.CreatedAt), ("updated_at", Now));

    private ulong FirstStageId() =>
        master.All("MStage").Select(s => MasterData.F<ulong>(s, "id")).DefaultIfEmpty(0UL).Min();

    // An empty helper list leaves no plate to tap before a battle: offer one NPC helper
    // (t_player_id 0 = NPC, so no helper is actually sent to battle/start).
    public object? HelpList(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var leader = p.Deck.Select(p.Character).OfType<OwnedCharacter>().FirstOrDefault() ?? p.Characters.FirstOrDefault();
        if (leader == null) return Obj(("help_players", Array.Empty<object>()));
        return Obj(("help_players", new List<object?>
        {
            Obj(("t_player_id", 0UL), ("name", "Offline"), ("comment", ""), ("rank", 1), ("relation", 0),
                ("friend_auto_accept_flg", false), ("t_character", Characters.ToWire(leader, 0)),
                ("m_character_id", leader.MCharacterId), ("last_play_at", Now), ("updated_at", Now),
                ("t_potential_kinds", Array.Empty<object>()), ("t_potential_class", SchemaWriter.Nil)),
        }));
    }

    // ---- Battle start -----------------------------------------------------------------------

    public object? Start(Player? p, JsonObject q, string method)
    {
        if (p == null) return null;
        var stageId = U(q, "m_stage_id");
        var deckNo = I(q, "t_deck_no", 1);
        var (waves, enemyIds) = Waves(stageId);

        p.BattleSeq++;
        Log.Info($"Battle start: stage {stageId}, {waves.Count} waves, {enemyIds.Count} enemy types.");
        var result = Obj(
            ("id", p.BattleSeq), ("updated_at", Now), ("t_player_id", p.Id), ("t_deck_no", deckNo),
            ("battle_type", BattleTypeNormal), ("m_stage_id", stageId),
            ("enemy_list", waves),
            ("m_enemies", enemyIds.Select(id => master.Get("MEnemy", id)).ToList()),
            ("stage_mission", Missions(p, stageId).Select(b => (object)(b ? 1 : 0)).ToArray()),
            ("t_stage", Obj(("id", stageId), ("t_player_id", p.Id), ("m_stage_id", stageId),
                ("clear_num", p.StageClears.GetValueOrDefault(stageId)), ("clear_flg", p.StageClears.ContainsKey(stageId)))),
            ("help_t_player_id", 0), ("help_t_character_id", 0), ("help_t_character_lv", 0),
            ("m_guest_character_id", U(q, "m_guest_character_id")),
            // Non-null values here switch the client to raid/event/arena behaviour.
            ("t_raid_status", SchemaWriter.Nil), ("t_character_ids", SchemaWriter.Nil), ("t_memory_ids", SchemaWriter.Nil),
            ("t_status", SchemaWriter.Nil), ("t_division_battle_status", SchemaWriter.Nil),
            ("help_player_character", SchemaWriter.Nil), ("before_params_help_character", SchemaWriter.Nil),
            ("enemyDataList", SchemaWriter.Nil), ("t_memories", SchemaWriter.Nil));
        return NilOtherObjects(method, result);
    }

    // One enemy group per wave (MStageEnemyGroup, weighted by rate), placed by MEnemyGroupPosition.
    private (List<object?> Waves, HashSet<ulong> EnemyIds) Waves(ulong stageId)
    {
        var waves = new List<object?>();
        var enemyIds = new HashSet<ulong>();
        var groupsByWave = master.All("MStageEnemyGroup")
            .Where(g => MasterData.F<ulong>(g, "m_stage_id") == stageId)
            .GroupBy(g => MasterData.F<int>(g, "wave")).OrderBy(g => g.Key);
        foreach (var wave in groupsByWave)
        {
            var group = Weighted(wave.ToList(), "rate");
            var slots = new ulong[5];
            if (group != null)
            {
                var groupId = MasterData.F<ulong>(group, "id");
                foreach (var pos in master.All("MEnemyGroupPosition").Where(x => MasterData.F<ulong>(x, "m_enemy_group_id") == groupId))
                {
                    var position = MasterData.F<int>(pos, "position");
                    var enemyId = MasterData.F<ulong>(pos, "m_enemy_id");
                    if (position is < 1 or > 5 || master.Get("MEnemy", enemyId) == null) continue;
                    slots[position - 1] = enemyId;
                    enemyIds.Add(enemyId);
                }
            }
            waves.Add(Obj(("pos1", slots[0]), ("pos2", slots[1]), ("pos3", slots[2]), ("pos4", slots[3]), ("pos5", slots[4])));
        }
        if (waves.Count == 0) Log.Warn($"Stage {stageId}: no enemy waves in master.");
        return (waves, enemyIds);
    }

    // ---- Battle end -------------------------------------------------------------------------

    public object? End(Player? p, JsonObject q, string method)
    {
        if (p == null) return null;
        if (I(q, "battle_type") == BattleTypeItemWorld) return ItemWorldEnd(p, q, method);
        var stageId = U(q, "m_stage_id");
        var win = I(q, "result", 0) == BattleResultWin;
        var stage = master.Get("MStage", stageId);
        var before = Missions(p, stageId).ToArray();
        var deck = p.Deck.Select(p.Character).OfType<OwnedCharacter>().ToList();

        long playerExp = 0;
        int quartz = 0;
        var after = before.ToArray();
        var s = new Spoils();
        if (win)
        {
            var first = !p.StageClears.ContainsKey(stageId);
            p.StageClears[stageId] = p.StageClears.GetValueOrDefault(stageId) + 1;
            p.CurrentStage = stageId;

            var flags = MissionFlags(q["common_battle_result"]?.ToString());
            for (var i = 0; i < 3; i++) after[i] = before[i] || flags[i];
            p.StageMissions[stageId] = after;

            playerExp = stage == null ? 0 : MasterData.F<long>(stage, "exp");
            Defeated(p, q, s);
            BonusGearDrop(p, playerExp, s.Drops, s.Weapons, s.Equipment);
            var rank = stage == null ? 1 : Math.Max(1, MasterData.F<int>(stage, "rank"));
            quartz = (int)(QuartzPerRank * rank + playerExp / StageExpPerQuartz)
                     + (first ? FirstClearQuartz : 0)
                     + MissionStarQuartz * Enumerable.Range(0, 3).Count(i => after[i] && !before[i]);
            if (stage != null) Progress.Add(p, Progress.AreaBattle, 1, MasterData.F<ulong>(stage, "m_area_id"));

            foreach (var c in deck) chars.AddExp(c, s.CharExp);
            AddPlayerExp(p, playerExp);
            p.Items[ItemIdHl] = p.Items.GetValueOrDefault(ItemIdHl) + s.Hl;
            p.FreeStone += quartz;
            Log.Info($"Battle won: stage {stageId}{(first ? " (first clear)" : "")}, +{s.CharExp} exp each, +{playerExp} rank exp, +{s.Hl} HL, +{quartz} quartz, {s.Drops.Count} drops.");
        }
        else
        {
            p.StageLosses[stageId] = p.StageLosses.GetValueOrDefault(stageId) + 1;
            Log.Info($"Battle lost: stage {stageId}.");
        }

        var result = Obj(
            ("after_t_stage_current", StageCurrent(p, stageId)),
            ("after_t_characters", deck.Select(c => (object?)Characters.ToWire(c, p.Id)).ToList()),
            ("stage_mission_before", before.Select(b => (object)(b ? 1 : 0)).ToArray()),
            ("stage_mission_after", after.Select(b => (object)(b ? 1 : 0)).ToArray()),
            ("player_exp", playerExp),
            ("after_t_status", status(p)),
            ("drop_result", DropResult(p, s)),
            ("after_t_items", s.ChangedItems.Select(id => (object?)ItemRow(p, id)).ToList()),
            ("learning_commands", Array.Empty<object>()),
            ("m_guest_character_id", U(q, "m_guest_character_id")),
            ("clear_m_area_id", 0), ("clear_m_episode_id", 0), ("clear_stage_rank", 0),
            ("help_player", SchemaWriter.Nil));
        return NilOtherObjects(method, result);
    }

    // What a won battle yields from its defeated enemies (battle_exp_data lists one entry per kill).
    private sealed class Spoils
    {
        public long CharExp, Hl;
        public readonly List<object?> Drops = new(), Characters = new(), Weapons = new(), Equipment = new();
        public readonly HashSet<ulong> ChangedItems = new() { ItemIdHl };
    }

    private void Defeated(Player p, JsonObject q, Spoils s)
    {
        var kills = (q["battle_exp_data"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        foreach (var kill in kills)
        {
            var enemy = master.Get("MEnemy", U(kill, "m_enemy_id"));
            if (enemy == null) continue;
            s.CharExp += MasterData.F<long>(enemy, "exp");
            var min = MasterData.F<int>(enemy, "drop_point_min");
            var max = Math.Max(min, MasterData.F<int>(enemy, "drop_point_max"));
            s.Hl += _rng.Next(min, max + 1) * HlMultiplier;
            RollDrops(p, enemy, s.Drops, s.Characters, s.Weapons, s.Equipment, s.ChangedItems);
        }
        Progress.Add(p, Progress.Battle);
        Progress.Add(p, Progress.Enemy, kills.Count);
        Progress.Add(p, Progress.Act, DrpgServer.Missions.ActPerBattle);
    }

    private static Dictionary<string, object?> DropResult(Player p, Spoils s) => Obj(
        ("drop_list", (s.Hl > 0 ? new List<object?> { Obj(("id", ItemIdHl), ("num", (int)Math.Min(s.Hl, int.MaxValue)), ("type", PresentTypeItem), ("rank", 0), ("rarity", 0)) } : new List<object?>()).Concat(s.Drops).ToList()),
        ("after_t_item", s.ChangedItems.Select(id => (object?)ItemRow(p, id)).ToList()),
        ("drop_character", s.Characters),
        ("drop_weapon", s.Weapons.Count > 0 ? Obj(("weapons", s.Weapons), ("weapon_innocents", Obj(("t_innocents", Array.Empty<object>())))) : SchemaWriter.Nil),
        ("drop_equipment", s.Equipment.Count > 0 ? Obj(("equipments", s.Equipment), ("equipment_innocents", Obj(("t_innocents", Array.Empty<object>())))) : SchemaWriter.Nil),
        ("stones", StoneRows(p)));

    // Few enemy tables contain weapons/equipment, so offline every win has a chance of one extra
    // piece of gear, its item rank scaled by how hard the stage is (sqrt of the stage exp).
    private const double BonusGearChance = 0.25;

    private void BonusGearDrop(Player p, long stageExp, List<object?> drops, List<object?> weapons, List<object?> equipment)
    {
        if (_rng.NextDouble() >= BonusGearChance) return;
        var kind = _rng.Next(2) == 0 ? PresentTypeWeapon : PresentTypeEquipment;
        var cap = 1 + (int)Math.Sqrt(Math.Max(0, stageExp));
        var pool = master.All(kind == PresentTypeWeapon ? "MWeapon" : "MEquipment")
            .Where(r => MasterData.F<int>(r, "item_rank") <= cap).ToList();
        if (pool.Count == 0) return;
        var id = MasterData.F<ulong>(pool[_rng.Next(pool.Count)], "id");
        var roll = _rng.NextDouble();
        var (band, value) = roll < 0.05 ? (3, _rng.Next(70, 100)) : roll < 0.30 ? (2, _rng.Next(40, 70)) : (1, _rng.Next(1, 40));
        var gear = shop.CreateDrop(p, kind, id, value);
        if (gear == null) return;
        (kind == PresentTypeWeapon ? weapons : equipment).Add(Shop.Wire(p, gear));
        drops.Add(Obj(("id", id), ("num", 1), ("type", kind), ("rank", 0), ("rarity", band)));
    }

    // MEnemy drop tables: parallel arrays of present type / id / rarity / num and a % rate.
    // Items go to the inventory, characters to the box, weapons/equipment are created (drop
    // rarity 1/2/3 = common/rare/legend band of the 1..99 rarity value).
    private void RollDrops(Player p, object enemy, List<object?> drops, List<object?> characters,
        List<object?> weapons, List<object?> equipment, HashSet<ulong> changedItems)
    {
        var typesArr = MasterData.A<int>(enemy, "drop_present_type");
        var ids = MasterData.A<ulong>(enemy, "drop_present_id");
        var rarities = MasterData.A<int>(enemy, "drop_present_rarity");
        var nums = MasterData.A<int>(enemy, "drop_present_num");
        var rates = MasterData.A<float>(enemy, "drop_present_rate");
        // The rates often add up to well over 100, so they are weights: an enemy drops one thing
        // with DropChance, picked by those weights.
        var count = Math.Min(typesArr.Length, Math.Min(ids.Length, rates.Length));
        var total = Enumerable.Range(0, count).Sum(i => Math.Max(0f, rates[i]));
        if (count == 0 || total <= 0 || _rng.NextDouble() >= DropChance) return;
        var roll = _rng.NextDouble() * total;
        var pick = count - 1;
        for (var k = 0; k < count; k++)
        {
            roll -= Math.Max(0f, rates[k]);
            if (roll < 0) { pick = k; break; }
        }
        foreach (var i in new[] { pick })
        {
            var type = typesArr[i];
            var id = ids[i];
            var rarity = i < rarities.Length ? rarities[i] : 1;
            var num = Math.Max(1, i < nums.Length ? nums[i] : 1);
            switch (type)
            {
                case PresentTypeItem when master.Get("MItem", id) != null:
                    p.Items[id] = p.Items.GetValueOrDefault(id) + num;
                    changedItems.Add(id);
                    break;
                case PresentTypeCharacter when master.Get("MCharacter", id) != null:
                    var c = chars.Create(p.NextCharacterId(), id);
                    p.Characters.Add(c);
                    characters.Add(Characters.ToWire(c, p.Id));
                    num = 1;
                    break;
                case PresentTypeWeapon or PresentTypeEquipment:
                    var value = rarity switch { <= 1 => _rng.Next(1, 40), 2 => _rng.Next(40, 70), 3 => _rng.Next(70, 100), _ => Math.Min(rarity, 99) };
                    var gear = shop.CreateDrop(p, type, id, value);
                    if (gear == null) continue;
                    (type == PresentTypeWeapon ? weapons : equipment).Add(Shop.Wire(p, gear));
                    num = 1;
                    break;
                default:
                    continue;
            }
            drops.Add(Obj(("id", id), ("num", num), ("type", type), ("rank", 0), ("rarity", rarity)));
        }
    }

    // Mission flags are in the JWT payload ("a,b,c" under hfbm784khk2639pf); the signature isn't checked.
    private static bool[] MissionFlags(string? jwt)
    {
        var flags = new bool[3];
        try
        {
            var parts = jwt?.Split('.');
            if (parts is not { Length: >= 2 }) return flags;
            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var payload = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            var values = payload?["hfbm784khk2639pf"]?.ToString()?.Split(',') ?? [];
            for (var i = 0; i < 3 && i < values.Length; i++) flags[i] = values[i].Trim() is "1" or "true";
        }
        catch (Exception e) { Log.Warn($"Could not read battle result token: {e.Message}"); }
        return flags;
    }

    private bool[] Missions(Player p, ulong stageId) =>
        p.StageMissions.TryGetValue(stageId, out var m) && m.Length == 3 ? m : new bool[3];

    // MPlayerRank: need_exp of rank r+1 = exp to go from r to r+1.
    private void AddPlayerExp(Player p, long exp)
    {
        if (exp <= 0) return;
        p.RankExpTotal += exp;
        p.RankExp += exp;
        var needs = master.All("MPlayerRank").ToDictionary(r => MasterData.F<int>(r, "rank"), r => MasterData.F<long>(r, "need_exp"));
        while (needs.TryGetValue(p.Rank + 1, out var need) && need > 0 && p.RankExp >= need)
        {
            p.RankExp -= need;
            p.Rank++;
        }
    }

    // ---- Story scenes -----------------------------------------------------------------------

    public object? Story(Player? p, JsonObject q, string method)
    {
        if (p == null) return null;
        var stageId = U(q, "m_stage_id");
        p.StageClears[stageId] = p.StageClears.GetValueOrDefault(stageId) + 1;
        p.CurrentStage = stageId;
        var stage = master.Get("MStage", stageId);
        var result = Obj(
            ("after_t_stage_current", StageCurrent(p, stageId)),
            ("clear_stages", stage == null ? new List<object?>() : new List<object?> { ClearStageRow(p, stage) }),
            // Must stay null unless an area/episode was really cleared (an empty one crashes).
            ("clear_areas", SchemaWriter.Nil), ("clear_episodes", SchemaWriter.Nil),
            ("clear_m_area_id", 0), ("clear_m_episode_id", 0), ("clear_stage_rank", 0),
            ("after_t_sub_tutorials", SchemaWriter.Nil));
        return NilOtherObjects(method, result);
    }

    // ---- Items --------------------------------------------------------------------------------

    public object? ItemList(Player? p, JsonObject q)
    {
        if (p == null) return null;
        if (I(q, "page", 1) > 1) return new List<object?>();
        return p.Items.Keys.Select(id => (object?)ItemRow(p, id)).ToList();
    }

    private static Dictionary<string, object?> ItemRow(Player p, ulong itemId) => Obj(
        ("id", itemId), ("m_item_id", itemId),
        ("num", p.Items.GetValueOrDefault(itemId)), ("num_total", p.Items.GetValueOrDefault(itemId)),
        ("updated_at", Now));

    private static List<object?> StoneRows(Player p) =>
    [
        Obj(("id", 1UL), ("stone_type", 1), ("num", p.FreeStone), ("num_used", p.FreeStoneUsed)),
        Obj(("id", 2UL), ("stone_type", 2), ("num", p.PaidStone), ("num_used", p.PaidStoneUsed)),
    ];

    // ---- Helpers ------------------------------------------------------------------------------

    // Battle responses carry many optional objects (raid, arena, tower, gate…) whose mere presence
    // changes client behaviour, so every object field not set explicitly goes out as nil.
    private Dictionary<string, object?> NilOtherObjects(string method, Dictionary<string, object?> result)
    {
        if (!types.ResultTypes.TryGetValue(method, out var type)) return result;
        foreach (var name in GameTypes.ObjectFieldNames(type))
            result.TryAdd(name, SchemaWriter.Nil);
        return result;
    }

    private object? Weighted(List<object> rows, string field)
    {
        var total = rows.Sum(r => (double)MasterData.F<ulong>(r, field));
        if (rows.Count == 0) return null;
        if (total <= 0) return rows[_rng.Next(rows.Count)];
        var roll = _rng.NextDouble() * total;
        foreach (var r in rows)
        {
            roll -= MasterData.F<ulong>(r, field);
            if (roll < 0) return r;
        }
        return rows[^1];
    }
}
