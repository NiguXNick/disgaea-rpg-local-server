namespace DrpgServer;

// A character owned by the player, as persisted in the save file.
public sealed class OwnedCharacter
{
    public ulong Id { get; set; }
    public ulong MCharacterId { get; set; }
    public int Rarity { get; set; }
    public int Lv { get; set; } = 1;
    public long Exp { get; set; }
    public int Hp { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public int Inte { get; set; }
    public int Res { get; set; }
    public int Spd { get; set; }
    public ulong LeaderSkillId { get; set; }
    public ulong[] Commands { get; set; } = new ulong[4];
    public string CreatedAt { get; set; } = "";
}

public sealed class Characters(MasterData master)
{
    // Stats follow the client's CharacterManager formula: min + per_lv * (lv - 1) (rarity correction 1, rebirth 0).
    public OwnedCharacter Create(ulong id, ulong mCharacterId, int? rarity = null, int lv = 1)
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
        int Stat(string name) => (int)Math.Ceiling(MasterData.F<double>(m, name + "_min") + MasterData.F<double>(m, name + "_per_lv") * (lv - 1));
        c.Rarity = rarity ?? MasterData.F<int>(m, "base_rare");
        c.Hp = Stat("hp"); c.Atk = Stat("atk"); c.Def = Stat("def"); c.Inte = Stat("inte"); c.Res = Stat("res");
        c.Spd = MasterData.F<int>(m, "spd_min");
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
        while (list.Count < 4) list.Add(list[0]); // no empty slots
        c.Commands = list.ToArray();
        return c;
    }

    // CharacterUserData as the client expects it (t_character_commands must match m_command_id_N).
    public static Dictionary<string, object?> ToWire(OwnedCharacter c, ulong playerId)
    {
        var commands = c.Commands.Select((cmd, i) => (cmd, i)).Where(x => x.cmd != 0)
            .Select(x => (object?)new Dictionary<string, object?>
            {
                ["id"] = c.Id * 10 + (ulong)x.i + 1,
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
            ["exp_total"] = c.Exp,
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
            ["created_at"] = c.CreatedAt,
            ["t_character_commands"] = commands,
        };
    }
}
