using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Character growth: reincarnation and skills.
public sealed class Growth(MasterData master, Characters chars)
{
    private const ulong ItemHl = 101;
    private const int RebirthGainMana = 100; // SyncDefineData.rebirth_gain_mana

    // character/rebirth: allowed at the level cap. Costs HL and materials from
    // MNecessaryRebirthMaterial (row for the character's type and the next rebirth). The level
    // goes back to 1 with a cap 100 higher, stats scale with the cap, +100 mana.
    // after_t_items and after_t_record must never be null.
    public object? Rebirth(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var c = p.Character(U(q, "t_character_id"));
        var changed = new HashSet<ulong>();
        if (c != null && c.Lv >= Characters.MaxLevel(c) && Characters.MaxLevel(c) < Characters.CharacterLvMax)
        {
            var type = master.Get("MCharacter", c.MCharacterId) is { } m ? MasterData.F<int>(m, "character_type") : 0;
            var cost = master.All("MNecessaryRebirthMaterial").FirstOrDefault(r =>
                MasterData.F<int>(r, "rebirth_type") == type && MasterData.F<int>(r, "rebirth_num") == c.RebirthNum + 1);
            if (cost != null)
            {
                Pay(p, ItemHl, MasterData.F<long>(cost, "necessary_hl_num"), changed);
                for (var i = 1; i <= 7; i++)
                    Pay(p, MasterData.F<ulong>(cost, $"m_item_id_{i}"), MasterData.F<long>(cost, $"necessary_num_{i}"), changed);
            }
            c.RebirthNum++;
            c.Lv = 1;
            c.Exp = 0;
            c.ExpTotal = 0;
            c.Mana += RebirthGainMana;
            chars.ApplyStats(c);
            Progress.Add(p, Progress.Rebirth);
            Log.Info($"Reincarnation: character {c.Id} -> rebirth {c.RebirthNum}, level cap {Characters.MaxLevel(c)}.");
        }
        return Obj(
            ("after_character", c == null ? SchemaWriter.Nil : Characters.ToWire(c, p.Id)),
            ("after_t_items", changed.Select(id => (object?)Rewards.ItemRow(p, id)).ToList()),
            ("after_t_record", Record(p)));
    }

    private static void Pay(Player p, ulong item, long num, HashSet<ulong> changed)
    {
        if (item == 0 || num <= 0) return;
        p.Items[item] = Math.Max(0, p.Items.GetValueOrDefault(item) - num);
        changed.Add(item);
    }

    // player/change_chara_command: m_command_ids = the 4 slots (0 = empty). Only learned skills.
    public object? ChangeCommands(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var c = p.Character(U(q, "t_character_id"));
        if (c == null) return null;
        var ids = (q["m_command_ids"] as JsonArray ?? []).Select(x => ulong.TryParse(x?.ToString(), out var v) ? v : 0).ToList();
        var slots = new ulong[4];
        for (var i = 0; i < 4 && i < ids.Count; i++)
            if (ids[i] != 0 && c.Learned.Contains(ids[i]) && !slots.Contains(ids[i])) slots[i] = ids[i];
        c.Commands = slots;
        Log.Info($"Skills: character {c.Id} -> {string.Join(", ", slots)}");
        return Characters.ToWire(c, p.Id);
    }

    // player/update_command_new_off: "new" marks are never set offline; just echo the character.
    public object? CommandNewOff(Player? p, JsonObject q) =>
        p?.Character(U(q, "id")) is { } c ? Characters.ToWire(c, p.Id) : null;
}
