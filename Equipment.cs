using System.Text.Json.Nodes;
using static DrpgServer.Handlers;

namespace DrpgServer;

// Equipping gear and gear presets ("equipment decks").
//
// The client works out who wears what only from each item's set_chara_id / set_no (weapon slot 0,
// equipment slots 0..2); the player's own characters are sent with null gear lists so the client
// builds them from the items. Responses carry the full rows of every item that changed (the
// client replaces its objects wholesale).
public sealed class Equipment
{
    private const int Weapon = Rewards.TypeWeapon, Armor = Rewards.TypeEquipment;
    private const int EquipmentSlots = 3, PartySize = 5;
    // The preset screen indexes its tabs by a saved position: the count must never shrink.
    private const int DeckCount = 10;

    // player/change_chara_equipment: set_weapon_id_list [w], set_equipment_id_list [e1, e2, e3]; 0 = empty.
    public object? Change(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var charId = U(q, "t_character_id");
        var touched = new HashSet<Gear>();
        Assign(p, charId, Weapon, Ids(q, "set_weapon_id_list"), touched);
        Assign(p, charId, Armor, Ids(q, "set_equipment_id_list"), touched);
        Log.Info($"Equipment: character {charId}, {touched.Count} items changed.");
        var c = p.Character(charId);
        return Obj(
            ("t_weapons", Rows(p, touched, Weapon)),
            ("t_equipments", Rows(p, touched, Armor)),
            ("t_characters", c == null ? new List<object?>() : new List<object?> { Characters.ToWire(c, p.Id) })); // never null
    }

    // Puts the requested items in slots 0..n-1 (0 = empty), taking them off whoever had them.
    private static void Assign(Player p, ulong charId, int kind, List<ulong> slots, HashSet<Gear> touched)
    {
        for (var slot = 0; slot < slots.Count; slot++)
        {
            var wanted = slots[slot];
            var current = p.Gear.FirstOrDefault(g => g.Kind == kind && g.SetCharaId == charId && g.SetNo == slot);
            if (current != null && current.Id != wanted)
            {
                current.SetCharaId = 0;
                current.SetNo = 0;
                touched.Add(current);
            }
            if (wanted == 0) continue;
            var item = p.Gear.FirstOrDefault(g => g.Kind == kind && g.Id == wanted);
            if (item == null || (item.SetCharaId == charId && item.SetNo == slot)) continue;
            item.SetCharaId = charId;
            item.SetNo = slot;
            touched.Add(item);
        }
    }

    // ---- Presets ------------------------------------------------------------------------------

    public object? Decks(Player? p, JsonObject q)
    {
        if (p == null) return null;
        if (I(q, "page", 1) > 1) return new List<object?>();
        return Enumerable.Range(1, DeckCount).Select(no => (object?)DeckRow(p, no)).ToList();
    }

    // weapon_equipment/update_equipment_deck: rename, set explicit contents (four arrays of 5),
    // copy a whole party's current gear (deck_no) or one character's (t_character_id + position).
    public object? UpdateDeck(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var no = Math.Clamp(I(q, "equipment_deck_no", 1), 1, DeckCount);
        var deck = Deck(p, no);
        var name = q["name"]?.ToString();
        if (!string.IsNullOrEmpty(name)) p.EquipmentDeckNames[no] = name;

        var partyNo = I(q, "deck_no");
        var charId = U(q, "t_character_id");
        var weapons = Ids(q, "t_weapon_ids");
        if (partyNo != 0)
        {
            var party = Party(p, partyNo);
            for (var i = 0; i < PartySize; i++) deck[i] = Worn(p, party[i]);
        }
        else if (charId != 0 && I(q, "character_position") is >= 1 and <= PartySize)
        {
            deck[I(q, "character_position") - 1] = Worn(p, charId);
        }
        else if (weapons.Count > 0)
        {
            var e1 = Ids(q, "t_equipment_ids1");
            var e2 = Ids(q, "t_equipment_ids2");
            var e3 = Ids(q, "t_equipment_ids3");
            ulong At(List<ulong> l, int i) => i < l.Count ? l[i] : 0;
            for (var i = 0; i < PartySize; i++) deck[i] = [At(weapons, i), At(e1, i), At(e2, i), At(e3, i)];
        }
        return Obj(("after_t_equipment_decks", new List<object?> { DeckRow(p, no) })); // non-empty: [0] is shown
    }

    // weapon_equipment/change_deck_equipments: applies a preset to a party (0 = keep the current item).
    public object? ApplyDeck(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var deck = Deck(p, Math.Clamp(I(q, "equipment_deck_no", 1), 1, DeckCount));
        var party = Party(p, I(q, "deck_no", p.SelectedDeckNo));
        var touched = new HashSet<Gear>();
        for (var i = 0; i < PartySize; i++)
        {
            var charId = party[i];
            if (charId == 0 || p.Character(charId) == null) continue;
            var worn = Worn(p, charId);
            Assign(p, charId, Weapon, [deck[i][0] != 0 ? deck[i][0] : worn[0]], touched);
            Assign(p, charId, Armor, Enumerable.Range(1, EquipmentSlots).Select(s => deck[i][s] != 0 ? deck[i][s] : worn[s]).ToList(), touched);
        }
        Log.Info($"Equipment preset applied: {touched.Count} items changed.");
        return Obj(
            ("after_t_weapons", Rows(p, touched, Weapon)),
            ("after_t_equipments", Rows(p, touched, Armor)),
            ("after_t_characters", SchemaWriter.Nil));
    }

    private static ulong[][] Deck(Player p, int no)
    {
        if (!p.EquipmentDecks.TryGetValue(no, out var d) || d.Length != PartySize)
            p.EquipmentDecks[no] = d = Enumerable.Range(0, PartySize).Select(_ => new ulong[1 + EquipmentSlots]).ToArray();
        return d;
    }

    // Ids that no longer exist (sold items) read as empty: the client looks every id up.
    private static Dictionary<string, object?> DeckRow(Player p, int no)
    {
        var deck = Deck(p, no);
        ulong Own(int kind, ulong id) => id != 0 && p.Gear.Any(g => g.Kind == kind && g.Id == id) ? id : 0;
        var row = Obj(("id", (ulong)no), ("t_player_id", p.Id), ("equipment_deck_no", no),
            ("name", p.EquipmentDeckNames.GetValueOrDefault(no, $"Equip Set {no}")));
        for (var i = 0; i < PartySize; i++)
            row[$"position{i + 1}"] = Obj(("t_weapon_id", Own(Weapon, deck[i][0])),
                ("t_equipment_id1", Own(Armor, deck[i][1])), ("t_equipment_id2", Own(Armor, deck[i][2])), ("t_equipment_id3", Own(Armor, deck[i][3])));
        return row;
    }

    // What a character wears now: [weapon, equipment 1..3].
    private static ulong[] Worn(Player p, ulong charId)
    {
        var worn = new ulong[1 + EquipmentSlots];
        if (charId == 0) return worn;
        foreach (var g in p.Gear.Where(g => g.SetCharaId == charId))
        {
            if (g.Kind == Weapon && g.SetNo == 0) worn[0] = g.Id;
            else if (g.Kind == Armor && g.SetNo is >= 0 and < EquipmentSlots) worn[1 + g.SetNo] = g.Id;
        }
        return worn;
    }

    // Party deck slots (same rules as player/decks).
    private static ulong[] Party(Player p, int deckNo)
    {
        var saved = p.Decks.TryGetValue(deckNo, out var d) ? d : deckNo == p.SelectedDeckNo ? p.Deck : new List<ulong>();
        return saved.Concat(Enumerable.Repeat(0UL, PartySize)).Take(PartySize).ToArray();
    }

    private static List<object?> Rows(Player p, HashSet<Gear> touched, int kind) =>
        touched.Where(g => g.Kind == kind).Select(g => (object?)Shop.Wire(p, g)).ToList();

    private static List<ulong> Ids(JsonObject q, string key) =>
        (q[key] as JsonArray ?? []).Select(x => ulong.TryParse(x?.ToString(), out var v) ? v : 0).ToList();
}
