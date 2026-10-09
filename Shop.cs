using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Equipment shop (daily lineup, buy, sell, renew) and the player's weapons/equipment.
//
// The shop screen waits until equipment_shop has a non-zero id and a lineup_date of today or
// later; prices and item stats use the same formulas as the client (MasterWeaponOrEquipmetBase,
// EquipmentView.calc_param) so what the player sees is what is charged.
public sealed class Shop(MasterData master)
{
    private const int Weapon = 3, Equipment = 4;    // present_type_weapon / present_type_equipment
    private const ulong ItemIdHl = 101;
    private const int ItemsPerKind = 15;
    private const int FreeRenewals = 5, RenewQuartz = 10;

    private readonly Random _rng = new();
    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd") + " 00:00:00";
    private static string Now => Time.Format(DateTime.UtcNow);

    // Offline the shop rank is unlocked up to the highest item rank.
    public int MaxShopRank => Math.Max(1, master.All("MWeapon").Concat(master.All("MEquipment"))
        .Select(r => MasterData.F<int>(r, "item_rank")).DefaultIfEmpty(1).Max());

    public object? Index(Player? p, JsonObject q)
    {
        if (p == null) return null;
        EnsureLineup(p);
        return Obj(("equipment_shop", ShopData(p)), ("garapon_lots", Array.Empty<object>()),
            ("shop_buy_products", Array.Empty<object>()), ("garapon_get_data", Garapon()));
    }

    public object? EquipmentShop(Player? p, JsonObject q)
    {
        if (p == null) return null;
        EnsureLineup(p);
        return ShopData(p);
    }

    public object? EquipmentItems(Player? p, JsonObject q)
    {
        if (p == null) return null;
        EnsureLineup(p);
        return p.ShopItems.Where(i => !i.Sold).Select(i => (object?)ItemWire(p, i)).ToList();
    }

    public object? Renew(Player? p, JsonObject q)
    {
        if (p == null) return null;
        EnsureLineup(p);
        object? stones = SchemaWriter.Nil;
        if (p.ShopUpdateNum >= FreeRenewals)
        {
            var paid = Math.Min(RenewQuartz, p.FreeStone - p.FreeStoneUsed);
            p.FreeStoneUsed += paid;
            Progress.Add(p, Progress.StoneSpent, paid);
            stones = StoneRows(p);
        }
        p.ShopUpdateNum++;
        Roll(p, Math.Clamp(I(q, "shop_rank", MaxShopRank), 1, MaxShopRank));
        return Obj(("equipment_shop", ShopData(p)),
            ("equipment_items", p.ShopItems.Select(i => (object?)ItemWire(p, i)).ToList()),
            ("after_stone_sum", stones));
    }

    public object? Buy(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var ids = (q["ids"] as JsonArray ?? []).Select(x => ulong.TryParse(x?.ToString(), out var v) ? v : 0).ToHashSet();
        var kind = I(q, "item_type");
        var weapons = new List<object?>();
        var equipment = new List<object?>();
        long cost = 0;
        foreach (var item in p.ShopItems.Where(i => ids.Contains(i.Id) && i.ItemType == kind && !i.Sold))
        {
            var m = MasterRow(item.ItemType, item.ItemId);
            if (m == null) continue;
            cost += BuyPrice(m, item.Rarity);
            item.Sold = true;
            var gear = CreateGear(p, item.ItemType, item.ItemId, item.Rarity, item.Pop, m);
            (item.ItemType == Weapon ? weapons : equipment).Add(GearWire(p, gear));
            // Rarity bands: 1 common (<40), 2 rare (<70), 3 legendary.
            Progress.Add(p, Progress.EquipBuy, 1, item.Rarity < 40 ? 1UL : item.Rarity < 70 ? 2UL : 3UL);
        }
        p.Items[ItemIdHl] = Math.Max(0, p.Items.GetValueOrDefault(ItemIdHl) - cost);
        Progress.Add(p, Progress.HlSpent, cost);
        Log.Info($"Shop: bought {weapons.Count + equipment.Count} items for {cost} HL.");
        return Obj(
            ("after_t_weapons", weapons.Count > 0 ? weapons : SchemaWriter.Nil),
            ("after_t_equipments", equipment.Count > 0 ? equipment : SchemaWriter.Nil),
            ("after_t_innocents", SchemaWriter.Nil),
            ("after_t_weapon_collections", SchemaWriter.Nil), ("after_t_equipment_collections", SchemaWriter.Nil),
            ("after_t_item", ItemRow(p, ItemIdHl)),
            ("after_garapon_ticket", Obj()),
            ("garapon_get_data", Garapon()), ("garapon_ticket_num", 0));
    }

    public object? Sell(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var sold = new List<object?>();
        long credit = 0;
        foreach (var s in (q["sell_equipments"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var kind = I(s, "eqtype") == 1 ? Weapon : Equipment;
            var id = U(s, "eqid");
            var gear = p.Gear.FirstOrDefault(g => g.Id == id && g.Kind == kind);
            if (gear == null) continue;
            var m = MasterRow(gear.Kind, gear.MId);
            if (m != null) credit += SellPrice(m, gear);
            p.Gear.Remove(gear);
            sold.Add(Obj(("eqtype", I(s, "eqtype")), ("eqid", id)));
        }
        p.Items[ItemIdHl] = p.Items.GetValueOrDefault(ItemIdHl) + credit;
        Log.Info($"Shop: sold {sold.Count} items for {credit} HL.");
        return Obj(("price", (int)Math.Min(credit, int.MaxValue)),
            ("after_t_item", new List<object?> { ItemRow(p, ItemIdHl) }),
            ("sell_equipments", sold));
    }

    public object? Weapons(Player? p, JsonObject q) => GearList(p, q, Weapon);
    public object? Equipments(Player? p, JsonObject q) => GearList(p, q, Equipment);

    private object? GearList(Player? p, JsonObject q, int kind)
    {
        if (p == null) return null;
        if (I(q, "page", 1) > 1) return new List<object?>();
        return p.Gear.Where(g => g.Kind == kind).Select(g => (object?)GearWire(p, g)).ToList();
    }

    // ---- Lineup -----------------------------------------------------------------------------

    private void EnsureLineup(Player p)
    {
        if (p.ShopLineupDate == Today && p.ShopItems.Count > 0) return;
        p.ShopLineupDate = Today;
        p.ShopUpdateNum = 0;
        Roll(p, MaxShopRank);
    }

    private void Roll(Player p, int shopRank)
    {
        p.ShopLineupNo++;
        p.ShopItems.Clear();
        ulong id = 1;
        foreach (var (kind, table) in new[] { (Weapon, "MWeapon"), (Equipment, "MEquipment") })
        {
            var pool = master.All(table).Where(r => MasterData.F<int>(r, "item_rank") <= shopRank).ToList();
            for (var i = 0; i < ItemsPerKind && pool.Count > 0; i++)
            {
                var row = pool[_rng.Next(pool.Count)];
                p.ShopItems.Add(new ShopItem
                {
                    Id = id++, ItemType = kind, ItemId = MasterData.F<ulong>(row, "id"),
                    Rarity = _rng.Next(1, 100), Pop = _rng.Next(0, 3),
                });
            }
        }
    }

    private static Dictionary<string, object?> ShopData(Player p) => Obj(
        ("id", p.Id), ("t_player_id", p.Id), ("lineup_date", p.ShopLineupDate), ("lineup_no", p.ShopLineupNo),
        ("lineup_update_num", p.ShopUpdateNum), ("created_at", p.CreatedAt), ("updated_at", Now));

    private static Dictionary<string, object?> ItemWire(Player p, ShopItem i) => Obj(
        ("id", i.Id), ("lineup_date", p.ShopLineupDate), ("lineup_no", p.ShopLineupNo),
        ("lineup_update_num", p.ShopUpdateNum), ("item_type", i.ItemType), ("item_id", i.ItemId),
        ("rarity", i.Rarity), ("pop", i.Pop), ("innocent_num", 0), ("sold_flg", i.Sold));

    private static Dictionary<string, object?> Garapon() =>
        Obj(("buy_num_now", 0), ("buy_num_ticket", 1000), ("buy_done_flg", false));

    // ---- Items --------------------------------------------------------------------------------

    private object? MasterRow(int kind, ulong id) => master.Get(kind == Weapon ? "MWeapon" : "MEquipment", id);

    // Stats as the shop preview shows them: ceil((min + per_stage*(lv-1)) * (1 + rarity/300)), spd without rarity.
    // Used by battle drops: creates the item if the master row exists, null otherwise.
    public Gear? CreateDrop(Player p, int kind, ulong mId, int rarity)
    {
        var m = MasterRow(kind, mId);
        return m == null ? null : CreateGear(p, kind, mId, rarity, _rng.Next(0, 3), m);
    }

    public static Dictionary<string, object?> Wire(Player p, Gear g) => GearWire(p, g);

    private Gear CreateGear(Player p, int kind, ulong mId, int rarity, int pop, object m)
    {
        var g = new Gear { Id = p.NextGearId++, Kind = kind, MId = mId, RarityValue = rarity, Pop = pop, CreatedAt = Now };
        g.LvMax = LevelAt(MaxStage(rarity));
        Recalc(g);
        p.Gear.Add(g);
        return g;
    }

    public void Recalc(Gear g)
    {
        var m = MasterRow(g.Kind, g.MId);
        if (m == null) return;
        int Stat(string name, int r) => (int)Math.Ceiling((MasterData.F<int>(m, name + "_min") + MasterData.F<int>(m, name + "_per_stage") * (g.Lv - 1)) * (1 + r / 300.0));
        g.Hp = Stat("hp", g.RarityValue); g.Atk = Stat("atk", g.RarityValue); g.Def = Stat("def", g.RarityValue);
        g.Inte = Stat("inte", g.RarityValue); g.Res = Stat("res", g.RarityValue); g.Spd = Stat("spd", 0);
    }

    // Item World depth by rarity value (SyncDefineData ITEM_WORLD_COMMON/RARE/LEGEND) and the item
    // level after clearing a floor (MWeaponEquipmentLevel: boss floors jump, e.g. floor 30 = Lv50).
    public static int MaxStage(int rarity) => rarity < 40 ? 30 : rarity < 70 ? 60 : 100;

    private Dictionary<int, int>? _levels;

    public int LevelAt(int stage)
    {
        _levels ??= master.All("MWeaponEquipmentLevel").GroupBy(r => MasterData.F<int>(r, "stage"))
            .ToDictionary(g => g.Key, g => g.Max(r => MasterData.F<int>(r, "lv")));
        return _levels.Where(kv => kv.Key <= stage).Select(kv => kv.Value).DefaultIfEmpty(1).Max();
    }

    public object? GearMaster(Gear g) => MasterRow(g.Kind, g.MId);

    // Items made before the Item World existed had a flat Lv cap of 10.
    public void RepairGear(Player p)
    {
        foreach (var g in p.Gear)
        {
            var max = LevelAt(MaxStage(g.RarityValue));
            if (g.LvMax != max) g.LvMax = max;
        }
    }

    // MasterWeaponOrEquipmetBase: price * ((lv + notObey + obey*2) * 0.05 + 1) * (1 + rarity/100).
    private static decimal Basic(object m, int rarity, int lv) =>
        MasterData.F<int>(m, "price") * ((lv) * 0.05m + 1) * (1 + rarity / 100m);

    private static long BuyPrice(object m, int rarity) => (long)Math.Ceiling(Basic(m, rarity, 1));
    private static long SellPrice(object m, Gear g) => (long)Math.Ceiling((float)Basic(m, g.RarityValue, g.Lv) / 10 * 3);

    private static Dictionary<string, object?> GearWire(Player p, Gear g)
    {
        var d = Obj(
            ("id", g.Id), ("t_player_id", p.Id), ("stage", g.Stage), ("pop", g.Pop), ("rarity_value", g.RarityValue),
            ("remake_count", 0), ("lv", g.Lv), ("lv_max", g.LvMax),
            ("hp", g.Hp), ("atk", g.Atk), ("def", g.Def), ("inte", g.Inte), ("res", g.Res), ("spd", g.Spd),
            ("set_chara_id", g.SetCharaId), ("set_no", g.SetNo), ("lock_flg", false), ("all_clear_flg", g.Stage >= MaxStage(g.RarityValue)), ("breeding_stage", 0),
            ("item_world_survey_end_at", ""), ("created_at", g.CreatedAt), ("innocent_auto_obey_flg", false), ("del_flg", false));
        d[g.Kind == Weapon ? "m_weapon_id" : "m_equipment_id"] = g.MId;
        return d;
    }

    private static Dictionary<string, object?> ItemRow(Player p, ulong itemId) => Obj(
        ("id", itemId), ("m_item_id", itemId), ("num", p.Items.GetValueOrDefault(itemId)),
        ("num_total", p.Items.GetValueOrDefault(itemId)), ("updated_at", Now));

    private static List<object?> StoneRows(Player p) =>
    [
        Obj(("id", 1UL), ("stone_type", 1), ("num", p.FreeStone), ("num_used", p.FreeStoneUsed)),
        Obj(("id", 2UL), ("stone_type", 2), ("num", p.PaidStone), ("num_used", p.PaidStoneUsed)),
    ];
}
