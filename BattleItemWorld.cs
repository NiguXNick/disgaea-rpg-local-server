using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Item World: every floor is one battle. item_world/start opens floor stage+1 of a weapon or
// piece of equipment; the normal battle/end (battle_type 5) finishes it and the item's stage,
// level and stats go back in after_t_weapon / after_t_equipment. "Go" on the result screen
// simply calls item_world/start again.
//
// The client has no enemy table for the Item World (the official server built the floors), so
// each floor borrows the enemy waves of a story stage whose recommended level matches the
// item's rank plus the floor. Innocents are not implemented yet: no floor has one.
public sealed partial class Battle
{
    private const int BattleTypeItemWorld = 5;          // battle_type_item_world
    private const int EquipmentTypeWeapon = 1;          // ItemWorldEngine: 1 weapon, 2 equipment
    private const int BossFloorQuartz = 10;

    public object? ItemWorldStart(Player? p, JsonObject q, string method)
    {
        if (p == null) return null;
        var type = I(q, "equipment_type");
        var gear = ItemWorldGear(p, U(q, "equipment_id"), type);
        if (gear == null)
        {
            Log.Warn($"Item World: item {U(q, "equipment_id")} (type {type}) not found.");
            return null;
        }
        var floor = Math.Min(gear.Stage + 1, Shop.MaxStage(gear.RarityValue));
        if (p.ItemWorldGearId != gear.Id || p.ItemWorldFloor == 0)
            Progress.Add(p, type == EquipmentTypeWeapon ? Progress.ItemWorldWeapon : Progress.ItemWorldEquipment);
        p.ItemWorldGearId = gear.Id;
        p.ItemWorldFloor = floor;

        var itemRank = shop.GearMaster(gear) is { } m ? MasterData.F<int>(m, "item_rank") : 1;
        var bossType = master.Get("MItemWorld", (ulong)floor) is { } w ? MasterData.F<int>(w, "boss_type") : 0;
        var stageId = FloorStage(itemRank + floor + (bossType > 0 ? floor / 5 : 0));
        var (waves, enemyIds) = Waves(stageId);

        p.BattleSeq++;
        Log.Info($"Item World: item {gear.Id} floor {floor}{(bossType > 0 ? " (boss)" : "")}, enemies of stage {stageId}.");
        var result = Obj(
            ("id", p.BattleSeq), ("updated_at", Now), ("t_player_id", p.Id), ("t_deck_no", I(q, "t_deck_no", 1)),
            ("battle_type", BattleTypeItemWorld),
            // m_stage_id 0: no stage mission menu. equipment_* are echoed into battle/end.
            ("m_stage_id", 0UL), ("equipment_id", gear.Id), ("equipment_type", type),
            ("stage", floor), // an MItemWorld id (background, music)
            ("t_innocent_id", 0UL),
            ("enemy_list", waves),
            ("m_enemies", enemyIds.Select(id => master.Get("MEnemy", id)).ToList()),
            ("stage_mission", SchemaWriter.Nil),
            ("help_t_player_id", 0), ("help_t_character_id", 0), ("help_t_character_lv", 0),
            ("t_raid_status", SchemaWriter.Nil), ("t_character_ids", SchemaWriter.Nil), ("t_memory_ids", SchemaWriter.Nil),
            ("t_status", SchemaWriter.Nil), ("t_division_battle_status", SchemaWriter.Nil),
            ("help_player_character", SchemaWriter.Nil), ("before_params_help_character", SchemaWriter.Nil),
            ("enemyDataList", SchemaWriter.Nil), ("t_memories", SchemaWriter.Nil));
        return NilOtherObjects(method, result);
    }

    private object? ItemWorldEnd(Player p, JsonObject q, string method)
    {
        var type = I(q, "equipment_type");
        var gear = ItemWorldGear(p, U(q, "equipment_id"), type);
        var win = I(q, "result", 0) == BattleResultWin;
        var deck = p.Deck.Select(p.Character).OfType<OwnedCharacter>().ToList();
        var s = new Spoils();
        int bossType = 0, quartz = 0;
        long playerExp = 0;

        if (win && gear != null)
        {
            var floor = p.ItemWorldGearId == gear.Id && p.ItemWorldFloor > 0 ? p.ItemWorldFloor : gear.Stage + 1;
            floor = Math.Min(floor, Shop.MaxStage(gear.RarityValue));
            bossType = master.Get("MItemWorld", (ulong)floor) is { } w ? MasterData.F<int>(w, "boss_type") : 0;
            gear.Stage = Math.Max(gear.Stage, floor);
            gear.Lv = Math.Min(gear.LvMax, shop.LevelAt(gear.Stage));
            shop.Recalc(gear);

            Defeated(p, q, s);
            playerExp = floor;
            quartz = 2 + floor / 5 + (bossType > 0 ? BossFloorQuartz : 0);
            foreach (var c in deck) chars.AddExp(c, s.CharExp);
            AddPlayerExp(p, playerExp);
            p.Items[ItemIdHl] = p.Items.GetValueOrDefault(ItemIdHl) + s.Hl;
            p.FreeStone += quartz;
            Progress.Add(p, Progress.ItemWorldClear);
            Progress.Max(p, Progress.ItemWorldFloor, floor);
            Log.Info($"Item World won: item {gear.Id} floor {floor} -> Lv{gear.Lv}/{gear.LvMax}, +{s.CharExp} exp each, +{s.Hl} HL, +{quartz} quartz.");
        }
        else
        {
            p.ItemWorldFloor = 0;
            Log.Info($"Item World {(win ? "result for an unknown item" : "lost")}.");
        }

        var gearWire = gear == null ? SchemaWriter.Nil : (object)Shop.Wire(p, gear);
        var weapon = type == EquipmentTypeWeapon;
        var result = Obj(
            ("after_t_characters", deck.Select(c => (object?)Characters.ToWire(c, p.Id)).ToList()),
            ("player_exp", playerExp),
            ("after_t_status", status(p)),
            ("after_t_record", Record(p)), // applied unconditionally: null breaks the home screen later
            ("after_t_weapon", weapon ? gearWire : SchemaWriter.Nil),
            ("after_t_equipment", weapon ? SchemaWriter.Nil : gearWire),
            ("boss_type", bossType),
            ("drop_result", DropResult(p, s)),
            ("after_t_items", s.ChangedItems.Select(id => (object?)ItemRow(p, id)).ToList()),
            ("learning_commands", Array.Empty<object>()),
            // Null unless meaningful: empty ones would add/remove innocent 0, or index [0] of an empty array.
            ("obey_innocent", SchemaWriter.Nil), ("remove_t_innocent", SchemaWriter.Nil), ("breeding_t_item", SchemaWriter.Nil),
            ("stage_mission_before", SchemaWriter.Nil), ("stage_mission_after", SchemaWriter.Nil),
            ("after_t_stage_current", SchemaWriter.Nil),
            ("clear_areas", SchemaWriter.Nil), ("clear_episodes", SchemaWriter.Nil),
            ("clear_m_area_id", 0), ("clear_m_episode_id", 0), ("clear_stage_rank", 0),
            ("help_player", SchemaWriter.Nil));
        return NilOtherObjects(method, result);
    }

    private static Gear? ItemWorldGear(Player p, ulong id, int equipmentType)
    {
        var kind = equipmentType == EquipmentTypeWeapon ? Rewards.TypeWeapon : Rewards.TypeEquipment;
        return p.Gear.FirstOrDefault(g => g.Id == id && g.Kind == kind);
    }

    // Story battle stages by recommended level (MStage.proper_level), for borrowing enemy waves.
    private List<(int Level, ulong Id)>? _floorStages;

    private ulong FloorStage(int level)
    {
        _floorStages ??= master.All("MStageEnemyGroup").Select(g => MasterData.F<ulong>(g, "m_stage_id")).Distinct()
            .Select(id => master.Get("MStage", id))
            .Where(s => s != null && int.TryParse(MasterData.F<string>(s, "proper_level"), out _))
            .Select(s => (int.Parse(MasterData.F<string>(s!, "proper_level")), MasterData.F<ulong>(s!, "id")))
            .OrderBy(x => x.Item1).ToList();
        if (_floorStages.Count == 0) return 0;
        var fits = _floorStages.Where(x => x.Level <= level).ToList();
        var band = fits.Count > 0 ? fits.Where(x => x.Level == fits[^1].Level).ToList() : [_floorStages[0]];
        return band[_rng.Next(band.Count)].Id;
    }
}
