using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Summon: gacha/available, gacha/sums, gacha/do and the Nether Quartz balance.
public sealed class Gacha(MasterData master, Characters chars)
{
    private const int GachaTypeTutorial = 2;
    private const int PriceTypePaidStone = 3;
    private const int PriceTypeTicket = 4;
    private const int ItemTypeStone = 2;
    private const int ItemTypeCharacter = 2;
    private const int MaxPulls = 10; // the result screen has 10 slots

    // Offline, the standard Premium Summon can give any character that was ever in a summon: the
    // banner's own rates pick the rarity, then any character of that rarity is drawn.
    private const ulong AllCharactersGachaId = 100001;
    private Dictionary<int, List<object>>? _allByRarity;

    private readonly Random _rng = new();

    // Banners shown on the summon screen. The client keeps an id only if it exists in MGacha,
    // isn't a tutorial gacha and is in term (master open_at/close_at); ticket-only banners also
    // need the ticket, and banners without count-bonus data crash its button setup.
    public object? Available(Player? p, JsonObject q)
    {
        var now = DateTime.UtcNow;
        var ids = master.All("MGacha")
            .Where(g => MasterData.F<int>(g, "gacha_type") != GachaTypeTutorial
                        && MasterData.F<int>(g, "price_type") != PriceTypeTicket
                        && MasterData.InTerm(g, now)
                        && MasterData.A<int>(g, "count_bonus_intervals").Length > 0
                        && MasterData.A<ulong>(g, "count_bonus_m_gacha_group_ids").Length > 0
                        && HasLots(MasterData.F<ulong>(g, "id")))
            .Select(g => (object?)MasterData.F<ulong>(g, "id"))
            .ToList();
        Log.Info($"Gacha: {ids.Count} banners available.");
        return Obj(("ids", ids), ("private_gachas", Array.Empty<object>()), ("drawable_gachas", Array.Empty<object>()));
    }

    public object? Sums(Player? p, JsonObject q) =>
        p?.GachaSums.Select(kv => (object?)SumWire(kv.Key, kv.Value)).ToList() ?? new List<object?>();

    public object? StoneSum(Player? p, JsonObject q) => p == null ? null : StoneRows(p);

    public object? Do(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var gachaId = (ulong)(q["m_gacha_id"] ?? 0);
        var num = Math.Clamp((int)(q["num"] ?? 1), 1, MaxPulls);
        var free = q["is_gacha_free"] is JsonNode f && (bool)f;
        var itemType = (int)(q["item_type"] ?? 0);
        var price = (long)(q["price"] ?? 0);

        var gacha = master.Get("MGacha", gachaId);
        if (gacha == null)
        {
            Log.Warn($"Gacha {gachaId} not in master.");
            return null;
        }

        var results = new List<object?>();
        var newChars = new List<object?>();
        var fixGroups = MasterData.A<ulong>(gacha, "fix_m_gacha_group_ids").Where(x => x != 0).ToArray();
        for (var i = 0; i < num; i++)
        {
            // Multi-pulls get their guaranteed group on the last slot.
            var guaranteed = num > 1 && i == num - 1 && fixGroups.Length > 0;
            var item = guaranteed ? RollFromGroups(fixGroups) : RollNormal(gachaId);
            if (item == null) break;
            if (gachaId == AllCharactersGachaId) item = AnyCharacterOfSameRarity(item);

            var mCharId = MasterData.F<ulong>(item, "item_id");
            var rarity = MasterData.F<int>(item, "rarity");
            var level = Math.Max(1, MasterData.F<int>(item, "level"));
            var effectRank = MasterData.F<int>(item, "effect_rank");
            var c = chars.Create(p.NextCharacterId(), mCharId, rarity > 0 ? rarity : null, level);
            p.Characters.Add(c);
            newChars.Add(Characters.ToWire(c, p.Id));
            results.Add(Obj(
                ("item_type", ItemTypeCharacter),
                ("item_id", mCharId),
                ("rarity", c.Rarity),
                ("level", level),
                ("max_level", Math.Max(level, MasterData.F<int>(item, "max_level"))),
                ("effect_rank", Math.Clamp(effectRank, 1, 4))));
        }
        if (results.Count == 0)
        {
            Log.Warn($"Gacha {gachaId}: nothing to roll.");
            return null;
        }

        if (!free && itemType == ItemTypeStone) Pay(p, price, MasterData.F<int>(gacha, "price_type") == PriceTypePaidStone);

        if (!p.GachaSums.TryGetValue(gachaId, out var sum)) p.GachaSums[gachaId] = sum = new GachaSum();
        sum.Sum += results.Count;
        sum.CountBonusDrawCount += results.Count;
        sum.SeriesDrawCount += results.Count;
        sum.TotalDrawCount += results.Count;
        // No zero padding: the hotfix's TimeZoneFix strips "00:00" out of this string.
        sum.LastDrawAt = DateTime.UtcNow.ToString("yyyy-M-d H:m:s");

        Log.Info($"Gacha {gachaId} x{results.Count}: {string.Join(", ", newChars.Cast<Dictionary<string, object?>>().Select(c => c["m_character_id"]))}");
        return Obj(
            ("gacha_result", results),
            ("after_t_characters", newChars),
            ("after_stone_sum", StoneRows(p)),
            ("after_gacha_sum", SumWire(gachaId, sum)),
            ("after_t_item", SchemaWriter.Nil),
            ("after_t_status", SchemaWriter.Nil),
            ("m_gacha_bonus_group_ids", SchemaWriter.Nil),
            ("review_flg", false),
            ("overflow_exchange_ticket", false));
    }

    private bool HasLots(ulong gachaId) =>
        master.All("MGachaLot").Any(l => MasterData.F<ulong>(l, "m_gacha_id") == gachaId);

    // MGachaLot picks a group by rate, then MGachaGroupItem picks an item in it by rate.
    // Non-character items are rerolled: every slot must become a character.
    private object? RollNormal(ulong gachaId)
    {
        var lots = master.All("MGachaLot").Where(l => MasterData.F<ulong>(l, "m_gacha_id") == gachaId).ToList();
        for (var attempt = 0; attempt < 20 && lots.Count > 0; attempt++)
        {
            var lot = Weighted(lots, "rate");
            var item = lot == null ? null : RollFromGroups([MasterData.F<ulong>(lot, "m_gacha_group_id")]);
            if (item != null) return item;
        }
        return null;
    }

    private object? RollFromGroups(ulong[] groups)
    {
        var items = master.All("MGachaGroupItem")
            .Where(x => groups.Contains(MasterData.F<ulong>(x, "m_gacha_group_id"))
                        && MasterData.F<int>(x, "rate") > 0
                        && MasterData.F<int>(x, "item_type") == ItemTypeCharacter
                        && master.Get("MCharacter", MasterData.F<ulong>(x, "item_id")) != null)
            .ToList();
        return Weighted(items, "rate");
    }

    // Every summonable character (one entry each), grouped by its summon rarity.
    private object AnyCharacterOfSameRarity(object rolled)
    {
        _allByRarity ??= master.All("MGachaGroupItem")
            .Where(x => MasterData.F<int>(x, "item_type") == ItemTypeCharacter
                        && MasterData.F<int>(x, "rate") > 0
                        && master.Get("MCharacter", MasterData.F<ulong>(x, "item_id")) != null)
            .GroupBy(x => MasterData.F<ulong>(x, "item_id"))
            .Select(g => g.First())
            .GroupBy(x => MasterData.F<int>(x, "rarity"))
            .ToDictionary(g => g.Key, g => g.ToList());
        var rarity = MasterData.F<int>(rolled, "rarity");
        return _allByRarity.TryGetValue(rarity, out var pool) && pool.Count > 0 ? pool[_rng.Next(pool.Count)] : rolled;
    }

    private object? Weighted(List<object> rows, string field)
    {
        var total = rows.Sum(r => (long)MasterData.F<int>(r, field));
        if (total <= 0) return null;
        var roll = (long)(_rng.NextDouble() * total);
        foreach (var r in rows)
        {
            roll -= MasterData.F<int>(r, field);
            if (roll < 0) return r;
        }
        return rows[^1];
    }

    // Free quartz is spent first, then paid (as the client's own debug response does).
    private static void Pay(Player p, long price, bool paidOnly)
    {
        if (!paidOnly)
        {
            var fromFree = Math.Min(price, p.FreeStone - p.FreeStoneUsed);
            p.FreeStoneUsed += fromFree;
            price -= fromFree;
        }
        p.PaidStoneUsed += Math.Min(price, p.PaidStone - p.PaidStoneUsed);
    }

    private static List<object?> StoneRows(Player p) =>
    [
        Obj(("id", 1UL), ("stone_type", 1), ("num", p.FreeStone), ("num_used", p.FreeStoneUsed)),
        Obj(("id", 2UL), ("stone_type", 2), ("num", p.PaidStone), ("num_used", p.PaidStoneUsed)),
    ];

    private static Dictionary<string, object?> SumWire(ulong gachaId, GachaSum s) => Obj(
        ("m_gacha_id", gachaId),
        ("sum", s.Sum),
        ("count_bonus_draw_count", s.CountBonusDrawCount),
        ("last_draw_at", s.LastDrawAt),
        ("series_draw_count", s.SeriesDrawCount),
        ("total_draw_count", s.TotalDrawCount));
}
