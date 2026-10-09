using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Gift box (present/*). Mission and trophy rewards are never applied by the client from the
// mission responses: they go into the gift box and are applied when the player receives them.
public sealed class Rewards(MasterData master, Characters chars, Shop shop)
{
    public const int TypeItem = 1, TypeCharacter = 2, TypeWeapon = 3, TypeEquipment = 4; // present_type_*
    private const ulong ItemHl = 101, ItemFreeQuartz = 201, ItemPaidQuartz = 202, ItemAp = 2501;
    private const int StatusPending = 0, StatusReceived = 1;
    private const int HistoryMax = 20; // the gift screen says the history keeps 20 items
    // delete_at must parse; gifts never expire offline.
    private const string NeverExpires = "2099-12-31 23:59:59";
    // GetGiftSortFilter conditions: which gifts a filter shows.
    private const int FilterStone = 0, FilterCharacter = 1, FilterEquipment = 2, FilterHl = 3, FilterAp = 4, FilterOther = 99;

    private readonly Random _rng = new();
    private static string Now => Time.Format(DateTime.UtcNow);

    public void Give(Player p, int type, ulong presentId, int rarity, long num, string message)
    {
        if (num <= 0) return;
        p.Gifts.Add(new Gift
        {
            Id = p.NextGiftId++, Type = type, PresentId = presentId, Rarity = rarity,
            Num = (int)Math.Min(num, int.MaxValue), Message = message, CreatedAt = Now,
        });
    }

    public static int PendingCount(Player p) => p.Gifts.Count(g => !g.Received);

    // is_limit_notice asks for gifts about to expire (home popup): none expire offline.
    public object? Index(Player? p, JsonObject q) =>
        p == null ? null : B(q, "is_limit_notice") ? new List<object?>() : PendingList(p, q);

    public object? History(Player? p, JsonObject q) => p == null ? null : HistoryList(p);

    public object? Receive(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var ids = (q["receive_ids"] as JsonArray ?? []).Select(x => ulong.TryParse(x?.ToString(), out var v) ? v : 0).ToHashSet();
        var received = new List<object?>();
        var items = new HashSet<ulong>();
        var characters = new List<object?>();
        var weapons = new List<object?>();
        var equipment = new List<object?>();
        var stones = false;

        foreach (var g in p.Gifts.Where(g => !g.Received && ids.Contains(g.Id)).ToList())
        {
            var granted = new Granted();
            if (!Grant(p, g.Type, g.PresentId, g.Rarity, g.Num, granted))
                Log.Warn($"Gift {g.Id}: present type {g.Type} id {g.PresentId} can't be given offline; marked received.");
            items.UnionWith(granted.Items);
            stones |= granted.Stones;
            characters.AddRange(granted.Characters);
            weapons.AddRange(granted.Weapons);
            equipment.AddRange(granted.Equipment);
            g.Received = true;
            g.ReceivedAt = Now;
            received.Add(g.Id);
        }

        // Keep only the newest received gifts (the history shows 20).
        var old = p.Gifts.Where(g => g.Received).OrderByDescending(g => g.ReceivedAt).ThenByDescending(g => g.Id).Skip(HistoryMax).ToHashSet();
        p.Gifts.RemoveAll(old.Contains);
        Progress.Add(p, Progress.Present, received.Count);
        Log.Info($"Gift box: received {received.Count} gifts.");

        // Non-null fields are applied as updates (an empty status would wipe the player's).
        return Obj(
            ("items", items.Count > 0 ? items.Select(id => (object?)ItemRow(p, id)).ToList() : SchemaWriter.Nil),
            ("stones", stones ? StoneRows(p) : SchemaWriter.Nil),
            ("characters", characters.Count > 0 ? characters : SchemaWriter.Nil),
            ("weapons", weapons.Count > 0 ? weapons : SchemaWriter.Nil),
            ("equipments", equipment.Count > 0 ? equipment : SchemaWriter.Nil),
            ("innocents", SchemaWriter.Nil),
            ("status", SchemaWriter.Nil),
            ("after_t_character_collections", SchemaWriter.Nil),
            ("after_t_weapon_collections", SchemaWriter.Nil),
            ("after_t_equipment_collections", SchemaWriter.Nil),
            ("after_t_home_customizes", SchemaWriter.Nil),
            ("after_t_events", SchemaWriter.Nil),
            ("add_act_num", 0),
            ("t_potential_kinds", SchemaWriter.Nil),
            ("t_potential_kind_conditions", SchemaWriter.Nil),
            ("duplicated_home_customize_ids", SchemaWriter.Nil),
            ("duplicated_memory_ids", SchemaWriter.Nil),
            ("after_t_memories", SchemaWriter.Nil),
            ("received_ids", received), // never null: the client reads its length
            ("present_list", PendingList(p, q)), // null would empty the client's list
            ("history_list", HistoryList(p)));
    }

    // What Grant changed, for the response's update fields.
    public sealed class Granted
    {
        public readonly HashSet<ulong> Items = new();
        public bool Stones;
        public readonly List<object?> Characters = new(), Weapons = new(), Equipment = new();
    }

    // Applies one present (gift, fleet catch, ...) to the save. False if it can't be given offline.
    public bool Grant(Player p, int type, ulong presentId, int rarity, int num, Granted g)
    {
        switch (type)
        {
            case TypeItem when presentId == ItemFreeQuartz:
                p.FreeStone += num;
                g.Stones = true;
                return true;
            case TypeItem when presentId == ItemPaidQuartz:
                p.PaidStone += num;
                g.Stones = true;
                return true;
            case TypeItem when presentId == ItemAp:
                return true; // AP is always full offline
            case TypeItem:
                p.Items[presentId] = p.Items.GetValueOrDefault(presentId) + num;
                g.Items.Add(presentId);
                return true;
            case TypeCharacter when master.Get("MCharacter", presentId) != null:
                for (var i = 0; i < num; i++)
                {
                    var c = chars.Create(p.NextCharacterId(), presentId, rarity > 0 ? rarity : null);
                    p.Characters.Add(c);
                    g.Characters.Add(Characters.ToWire(c, p.Id));
                }
                return true;
            case TypeWeapon or TypeEquipment:
                for (var i = 0; i < num; i++)
                {
                    var gear = shop.CreateDrop(p, type, presentId, RarityValue(rarity));
                    if (gear != null) (type == TypeWeapon ? g.Weapons : g.Equipment).Add(Shop.Wire(p, gear));
                }
                return true;
            default:
                return false;
        }
    }

    private List<object?> PendingList(Player p, JsonObject q)
    {
        var filter = (q["conditions"] as JsonArray ?? []).Select(x => int.TryParse(x?.ToString(), out var v) ? v : -1).ToHashSet();
        var gifts = p.Gifts.Where(g => !g.Received && (filter.Count == 0 || filter.Contains(Category(g))));
        gifts = I(q, "order") == 1 ? gifts.OrderByDescending(g => g.Id) : gifts.OrderBy(g => g.Id);
        return gifts.Select(g => (object?)Wire(p, g)).ToList();
    }

    private static List<object?> HistoryList(Player p) =>
        p.Gifts.Where(g => g.Received).OrderByDescending(g => g.ReceivedAt).ThenByDescending(g => g.Id)
            .Take(HistoryMax).Select(g => (object?)Wire(p, g)).ToList();

    private static int Category(Gift g) => g.Type switch
    {
        TypeItem when g.PresentId is ItemFreeQuartz or ItemPaidQuartz => FilterStone,
        TypeItem when g.PresentId == ItemHl => FilterHl,
        TypeItem when g.PresentId == ItemAp => FilterAp,
        TypeCharacter => FilterCharacter,
        TypeWeapon or TypeEquipment => FilterEquipment,
        _ => FilterOther,
    };

    private static Dictionary<string, object?> Wire(Player p, Gift g) => Obj(
        ("id", g.Id), ("t_player_id", p.Id), ("message", g.Message),
        ("present_type", g.Type), ("present_id", g.PresentId), ("present_rarity", g.Rarity), ("present_num", g.Num),
        ("status", g.Received ? StatusReceived : StatusPending),
        ("delete_at", NeverExpires), ("received_at", g.ReceivedAt), ("created_at", g.CreatedAt), ("updated_at", g.ReceivedAt is "" ? g.CreatedAt : g.ReceivedAt),
        ("reward_data", SchemaWriter.Nil)); // client cache

    // Gear rarity 1/2/3 = common/rare/legend band of the 1..99 rarity value.
    private int RarityValue(int rarity) => rarity switch
    {
        <= 1 => _rng.Next(1, 40),
        2 => _rng.Next(40, 70),
        3 => _rng.Next(70, 100),
        _ => Math.Min(rarity, 99),
    };

    internal static Dictionary<string, object?> ItemRow(Player p, ulong itemId) => Obj(
        ("id", itemId), ("m_item_id", itemId), ("num", p.Items.GetValueOrDefault(itemId)),
        ("num_total", p.Items.GetValueOrDefault(itemId)), ("updated_at", Now));

    internal static List<object?> StoneRows(Player p) =>
    [
        Obj(("id", 1UL), ("stone_type", 1), ("num", p.FreeStone), ("num_used", p.FreeStoneUsed)),
        Obj(("id", 2UL), ("stone_type", 2), ("num", p.PaidStone), ("num_used", p.PaidStoneUsed)),
    ];
}
