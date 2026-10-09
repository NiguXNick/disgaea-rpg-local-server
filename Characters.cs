namespace DrpgServer;

// A character owned by the player, as persisted in the save file.
public sealed class OwnedCharacter
{
    public ulong Id { get; set; }
    public ulong MCharacterId { get; set; }
    public int Rarity { get; set; }
    public int Lv { get; set; } = 1;
    public long Exp { get; set; }      // progress within the current level
    public long ExpTotal { get; set; } // cumulative
    public int Hp { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public int Inte { get; set; }
    public int Res { get; set; }
    public int Spd { get; set; }
    public ulong LeaderSkillId { get; set; }
    public ulong[] Commands { get; set; } = new ulong[4];        // equipped skills (slots 1..4)
    public List<ulong> Learned { get; set; } = new();             // learned skills, append-only (row ids follow the order)
    public int RebirthNum { get; set; }
    public int Mana { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class Characters(MasterData master)
{
    // Stats follow the client's CharacterManager formula: min + per_lv * (lv - 1) (rarity correction 1, rebirth 0).
    public OwnedCharacter Create(ulong id, ulong mCharacterId, int? rarity = null, int lv = 1, bool padSlots = false)
    {
        var m = master.Get("MCharacter", mCharacterId);
        var c = new OwnedCharacter
        {
            Id = id,
            MCharacterId = mCharacterId,
            Lv = lv,
            CreatedAt = Time.Format(DateTime.UtcNow),
        };
        if (m == null)
        {
            Log.Warn($"Character {mCharacterId} is not in the master data; using generic stats.");
            c.Rarity = rarity ?? 1;
            c.Hp = 100; c.Atk = 30; c.Def = 30; c.Inte = 30; c.Res = 30; c.Spd = 30;
            return c;
        }
        c.Rarity = rarity ?? MasterData.F<int>(m, "base_rare");
        ApplyStats(c, m);
        c.LeaderSkillId = MasterData.F<ulong>(m, "m_leader_skill_id");

        // Commands available at this level first, then the character's next ones: the battle result
        // screen walks a fixed 4-slot array and crashes on empty slots (BattleResultCharacterStatusView).
        var own = master.All("MCharacterCommand")
            .Where(r => MasterData.F<ulong>(r, "m_character_id") == mCharacterId)
            .OrderBy(r => MasterData.F<int>(r, "lv") <= lv ? 0 : 1)
            .ThenBy(r => MasterData.F<int>(r, "learn_type"))
            .ThenBy(r => MasterData.F<int>(r, "lv")).ThenBy(r => MasterData.F<ulong>(r, "id"))
            .Select(r => MasterData.F<ulong>(r, "m_command_id"));
        // Retrofit entries of type 1 (retrofit_type_acquire_command) unlock further commands.
        var retrofit = master.All("MCharacterRetrofit")
            .Where(r => MasterData.F<ulong>(r, "m_character_id") == mCharacterId && MasterData.F<int>(r, "retrofit_type") == 1)
            .OrderBy(r => MasterData.F<ulong>(r, "order_no"))
            .Select(r => (ulong)MasterData.F<int>(r, "retrofit_value"))
            .Where(id => master.Get("MCommand", id) != null);
        var list = own.Concat(retrofit).Distinct().Take(4).ToList();
        if (list.Count == 0)
        {
            Log.Warn($"Character {mCharacterId}: no commands in the master data.");
            c.Commands = new ulong[4];
            return c;
        }
        // The tutorial's battle result walks a fixed 4-slot array and crashes on empty slots, so
        // tutorial characters repeat their first command; elsewhere duplicates break the command
        // screens (SortCommandController keys by command id) and slots stay empty.
        if (padSlots) while (list.Count < 4) list.Add(list[0]);
        c.Commands = list.Concat(Enumerable.Repeat(0UL, 4)).Take(4).ToArray();
        Learn(c);
        return c;
    }

    // Learned skills: MCharacterCommand rows learned by level (learn_type 1) up to the current
    // level, plus whatever is equipped (every equipped skill needs a t_character_commands row).
    public void Learn(OwnedCharacter c)
    {
        var byLevel = master.All("MCharacterCommand")
            .Where(r => MasterData.F<ulong>(r, "m_character_id") == c.MCharacterId && MasterData.F<int>(r, "learn_type") == LearnTypeLevel
                        && MasterData.F<int>(r, "lv") <= c.Lv)
            .OrderBy(r => MasterData.F<int>(r, "lv")).ThenBy(r => MasterData.F<ulong>(r, "id"))
            .Select(r => MasterData.F<ulong>(r, "m_command_id"));
        foreach (var id in c.Commands.Where(x => x != 0).Concat(byLevel))
            if (!c.Learned.Contains(id) && master.Get("MCommand", id) != null) c.Learned.Add(id);
    }

    private const int LearnTypeLevel = 1; // SyncDefineData.learn_type_lv

    // CharacterManager.GetFixedParameter without status-up: the level cap / 100 is a multiplier
    // (each reincarnation makes the character stronger at the same level).
    public void ApplyStats(OwnedCharacter c)
    {
        var m = master.Get("MCharacter", c.MCharacterId);
        if (m == null) return;
        var cap = MaxLevel(c) / 100.0;
        int Stat(string name) => (int)Math.Ceiling((MasterData.F<double>(m, name + "_min") + MasterData.F<double>(m, name + "_per_lv") * (c.Lv - 1)) * cap);
        c.Hp = Stat("hp"); c.Atk = Stat("atk"); c.Def = Stat("def"); c.Inte = Stat("inte"); c.Res = Stat("res");
        c.Spd = MasterData.F<int>(m, "spd_min");
    }

    private void ApplyStats(OwnedCharacter c, object m) => ApplyStats(c);

    // Level cap: rebirth_rise_lv (100) more per reincarnation, up to character_lv_max (9999)
    // (CharacterUtility.GetMaxLv).
    internal const int RebirthRiseLv = 100, CharacterLvMax = 9999;
    public static int MaxLevel(OwnedCharacter c) => Math.Clamp(RebirthRiseLv + c.RebirthNum * RebirthRiseLv, 1, CharacterLvMax);

    // Adds exp and levels up with MCharacterLevel (need_exp of level L = exp to go from L-1 to L;
    // levels already reached in an earlier life cost half). Stats are recomputed and skills learned.
    public bool AddExp(OwnedCharacter c, long exp)
    {
        if (exp <= 0) return false;
        var start = c.Lv;
        var cap = MaxLevel(c);
        c.ExpTotal += exp;
        c.Exp += exp;
        var needs = master.All("MCharacterLevel").ToDictionary(r => MasterData.F<int>(r, "lv"), r => MasterData.F<long>(r, "need_exp"));
        long Need(int lv) => needs.TryGetValue(lv, out var n) ? (lv <= c.RebirthNum * RebirthRiseLv ? n / 2 : n) : 0;
        while (c.Lv < cap && Need(c.Lv + 1) > 0 && c.Exp >= Need(c.Lv + 1))
        {
            c.Exp -= Need(c.Lv + 1);
            c.Lv++;
        }
        if (c.Lv >= cap) c.Exp = 0;
        if (c.Lv == start) return false;
        ApplyStats(c);
        Learn(c);
        return true;
    }

    // CharacterUserData as the client expects it (t_character_commands must match m_command_id_N).
    public static Dictionary<string, object?> ToWire(OwnedCharacter c, ulong playerId)
    {
        // One row per learned skill (never null; every equipped skill must have one).
        var learned = c.Learned.Concat(c.Commands.Where(x => x != 0 && !c.Learned.Contains(x))).ToList();
        var commands = learned.Select((cmd, i) => (cmd, i))
            .Select(x => (object?)new Dictionary<string, object?>
            {
                ["id"] = c.Id * 1000 + (ulong)x.i + 1,
                ["t_player_id"] = playerId,
                ["t_character_id"] = c.Id,
                ["m_command_id"] = x.cmd,
                ["lv"] = 1,
                ["exp"] = 0,
                ["mana_flg"] = false,
                ["new_flg"] = false,
            }).ToList();

        return new Dictionary<string, object?>
        {
            ["id"] = c.Id,
            ["t_character_id"] = c.Id,
            ["t_player_id"] = playerId,
            ["m_character_id"] = c.MCharacterId,
            ["rarity"] = c.Rarity,
            ["m_leader_skill_id"] = c.LeaderSkillId,
            ["m_leader_skill_lv"] = 1,
            ["lv"] = c.Lv,
            ["exp"] = c.Exp,
            ["exp_total"] = c.ExpTotal,
            ["hp"] = c.Hp,
            ["atk"] = c.Atk,
            ["def"] = c.Def,
            ["inte"] = c.Inte,
            ["res"] = c.Res,
            ["spd"] = c.Spd,
            ["m_command_id_1"] = c.Commands[0],
            ["m_command_id_2"] = c.Commands[1],
            ["m_command_id_3"] = c.Commands[2],
            ["m_command_id_4"] = c.Commands[3],
            ["m_character_retrofit_ids"] = Array.Empty<object>(),
            ["rebirth_num"] = c.RebirthNum,
            ["mana"] = c.Mana,
            ["created_at"] = c.CreatedAt,
            ["t_character_commands"] = commands,
            // The player's own characters must have null gear lists: the client then builds them
            // from each item's set_chara_id/set_no (an empty list would hide equipped gear).
            // Guests (t_player_id 0, e.g. the NPC helper) only use the attached lists.
            ["weapons"] = playerId != 0 ? SchemaWriter.Nil : Array.Empty<object>(),
            ["equipments"] = playerId != 0 ? SchemaWriter.Nil : Array.Empty<object>(),
            ["weapon_effects"] = playerId != 0 ? SchemaWriter.Nil : Array.Empty<object>(),
            ["equipment_effects"] = playerId != 0 ? SchemaWriter.Nil : Array.Empty<object>(),
            ["innocents"] = playerId != 0 ? SchemaWriter.Nil : Array.Empty<object>(),
        };
    }
}
