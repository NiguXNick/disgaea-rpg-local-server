using System.Text.Json.Nodes;

namespace DrpgServer;

// Game logic for RPC methods. Anything not handled here still gets a well-formed
// default response built from the client's own result type.
public sealed class Handlers
{
    private static readonly object Nil = SchemaWriter.Nil;
    private const int TutorialEnd = 11; // SyncDefineData.tutorial_step_tutoend

    // Tutorial gacha pool and the four characters the tutorial's auto-select looks up by
    // m_character_id (TutorialSelectCharacterController).
    private static readonly ulong[] TutorialGachaPool = [20002];
    private static readonly ulong[] TutorialChoices = [10001, 30001, 10009, 10014];

    private readonly Characters _chars;
    private readonly Dictionary<string, Func<Player?, JsonObject, object?>> _map = new();

    public Handlers(GameTypes types, PlayerStore players)
    {
        _chars = new Characters(new MasterData(types));

        _map["player/add"] = (p, q) => Obj();
        _map["app/constants"] = (p, q) => null; // SyncDefineData defaults come from the class itself
        _map["battle/status"] = (p, q) => Nil;
        _map["player/sync"] = (p, q) => Array.Empty<object>();

        _map["player/tutorial"] = Tutorial;
        _map["player/tutorial_gacha_single"] = TutorialGachaSingle;
        _map["player/tutorial_choice_characters"] = (p, q) => Obj(("character_user_datas",
            TutorialChoices.Select(m => (object?)Characters.ToWire(_chars.Create(m, m), p?.Id ?? 0)).ToList()));

        _map["player/profile"] = (p, q) => p == null ? null : Profile(p);
        _map["player/index"] = PlayerIndex;
        _map["player/characters"] = (p, q) => p?.Characters.Select(c => (object?)Characters.ToWire(c, p.Id)).ToList();
        _map["player/decks"] = Decks;
    }

    public bool TryHandle(string method, Player? p, JsonObject prms, out object? result)
    {
        if (p != null) RepairCharacters(p);
        if (_map.TryGetValue(method, out var h))
        {
            result = h(p, prms);
            return true;
        }
        result = null;
        return false;
    }

    // Saves made before every command slot was filled would crash the battle result screen.
    private void RepairCharacters(Player p)
    {
        foreach (var c in p.Characters.Where(c => c.Commands.Any(x => x == 0)))
            c.Commands = _chars.Create(c.Id, c.MCharacterId, c.Rarity, c.Lv).Commands;
    }

    // Default value for an unimplemented method: empty object / empty collection.
    public static object Empty(Type t) =>
        t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            ? Array.Empty<object>()
            : new Dictionary<string, object?>();

    // ---- Tutorial -------------------------------------------------------------------------
    // Steps: 0 prologue, 1 name+gacha, 2 stage select, 3 battle 1, 4 prinny, 5 battle 2,
    // 6 introduction (+character choice), 7 battle 3, 8 epilogue, then 9 = finish.
    // Steps 2 and 7 only advance with gacha_fix=true; 4/6/8 come back from battles.
    private object? Tutorial(Player? p, JsonObject q)
    {
        if (p == null) return null;
        object? battleResult = Nil;

        // Resuming from the title right after a battle (steps 4/6/8) would need that battle's
        // result, which isn't kept: replay the battle instead.
        if (!q.ContainsKey("step") && p.TutorialStep is 4 or 6 or 8)
            p.TutorialStep--;

        if (q.ContainsKey("step"))
        {
            var step = (int)q["step"]!;
            var gachaFix = q["gacha_fix"] is JsonNode g && (bool)g;
            var ids = (q["charaIdList"] as JsonArray)?.Select(x => (ulong)x!).ToList() ?? new List<ulong>();
            var name = (string?)q["name"];
            if (!string.IsNullOrEmpty(name)) p.Name = name;

            switch (step)
            {
                case 2 when !gachaFix:
                    step = 1; // gacha animation still running
                    break;
                case 2:
                    var hero = _chars.Create(1, ids.FirstOrDefault(TutorialGachaPool[0]), p.GachaRarity > 0 ? p.GachaRarity : null);
                    p.Characters = [hero];
                    p.Deck = [hero.Id];
                    break;
                case 7 when !gachaFix:
                    step = 6; // character selection screen
                    break;
                case 7:
                    foreach (var m in ids.Where(m => p.Character(m) == null))
                        p.Characters.Add(_chars.Create(m, m));
                    p.Deck = new[] { 1UL }.Concat(ids.Where(id => id != 1 && p.Character(id) != null)).Take(5).ToList();
                    break;
                case 4 or 6 or 8:
                    battleResult = BattleResult(p);
                    break;
                case >= 9:
                    var deck = ids.Where(id => p.Character(id) != null).Distinct().Take(5).ToList();
                    if (deck.Count > 0) p.Deck = deck;
                    p.IsTutorial = false;
                    step = TutorialEnd;
                    break;
            }
            p.TutorialStep = step;
            Log.Info($"Tutorial: passo {step}{(p.IsTutorial ? "" : " (concluído)")}");
        }

        var heroM = p.Character(1)?.MCharacterId;
        return Obj(
            ("id", p.Id),
            ("t_player_id", p.Id),
            ("player_rank", 1),
            ("step", p.TutorialStep),
            ("is_tutorial", p.IsTutorial),
            ("name", p.Name),
            ("updated_at", Time.Format(DateTime.UtcNow)),
            ("gacha_character_ids", heroM == null ? Array.Empty<object>() : new object[] { heroM.Value }),
            ("gacha_character_rarities", heroM == null ? Array.Empty<object>() : new object[] { p.Character(1)!.Rarity }),
            ("gacha_character_effect_ranks", heroM == null ? Array.Empty<object>() : new object[] { 1 }),
            // UpdateTutorialData falls back to a default character/deck only when these are null.
            ("tutorial_characters", p.Characters.Count == 0 ? Nil : DeckFirst(p).Select(c => (object?)Characters.ToWire(c, p.Id)).ToList()),
            ("deck_character_ids", p.Deck.Count == 0 ? Nil : p.Deck.Cast<object>().ToList()),
            ("battle_result", battleResult));
    }

    private object? TutorialGachaSingle(Player? p, JsonObject q)
    {
        return TutorialGachaPool.Select(m =>
        {
            var c = _chars.Create(0, m);
            if (p != null) p.GachaRarity = c.Rarity;
            return (object?)Obj(("gacha_character_id", m), ("gacha_character_rarity", c.Rarity), ("gacha_character_effect_rank", 1));
        }).ToList();
    }

    // Tutorial battles run client-side; the result screen only needs the characters back.
    private static Dictionary<string, object?> BattleResult(Player p) => Obj(
        ("after_t_characters", DeckFirst(p).Select(c => (object?)Characters.ToWire(c, p.Id)).ToList()),
        ("player_exp", 0L),
        ("stage_mission_before", new object[] { 0, 0, 0 }),
        ("stage_mission_after", new object[] { 1, 1, 1 }),
        ("m_guest_character_id", 0UL),
        ("drop_result", Nil));

    private static IEnumerable<OwnedCharacter> DeckFirst(Player p) =>
        p.Deck.Select(p.Character).OfType<OwnedCharacter>().Concat(p.Characters.Where(c => !p.Deck.Contains(c.Id)));

    // ---- Player data (after the tutorial) ---------------------------------------------------
    private static Dictionary<string, object?> Profile(Player p) => Obj(
        ("id", p.Id),
        ("public_id", p.PublicId), // must be non-empty or the hotfix flips the client back into tutorial mode
        ("inherit_code", ""),
        ("name", p.Name),
        ("status", p.IsTutorial ? 0 : 1),
        ("install_date", p.CreatedAt),
        ("comment", ""),
        ("is_main_device", true),
        ("beginner_boost_end_at", ""));

    private object? PlayerIndex(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var leader = p.Deck.Count > 0 ? p.Character(p.Deck[0]) : p.Characters.FirstOrDefault();
        var now = Time.Format(DateTime.UtcNow);
        return Obj(
            ("player_adjusts", Array.Empty<object>()),
            ("profile", Profile(p)),
            ("record", Obj(("id", p.Id), ("t_player_id", p.Id))),
            ("player_setting", Obj(("id", p.Id), ("created_at", p.CreatedAt), ("updated_at", now))),
            ("status", Obj(
                ("id", p.Id), ("t_player_id", p.Id), ("rank", 1), ("exp", 0L), ("exp_total", 0L),
                ("act", 100), ("act_max", 100), ("act_at", now),
                ("character_max", 200), ("weapon_max", 200), ("equipment_max", 200), ("innocent_store_max", 200),
                ("deck_no", 1),
                ("favorite_char_id", leader?.Id ?? 0), ("favorite_m_char_id", leader?.MCharacterId ?? 0),
                ("agenda_confirm_at", now), ("last_free_gacha_at", ""), ("verify_age_date", ""))),
            ("act_give_count", Obj()),
            ("player_arena", Obj(("id", p.Id), ("act_at", now))));
    }

    private object? Decks(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var ids = p.Deck.Concat(Enumerable.Repeat(0UL, 5)).Take(5).ToArray();
        var leader = p.Character(ids[0]);
        return new List<object?>
        {
            Obj(("id", 1UL), ("t_player_id", p.Id), ("deck_no", 1), ("name", ""),
                ("leader_t_character_id", ids[0]), ("leader_m_character_id", leader?.MCharacterId ?? 0),
                ("t_character_ids", Obj(("pos1", ids[0]), ("pos2", ids[1]), ("pos3", ids[2]), ("pos4", ids[3]), ("pos5", ids[4]))),
                ("t_memory_ids", Array.Empty<object>()),
                ("created_at", p.CreatedAt), ("updated_at", p.CreatedAt)),
        };
    }

    private static Dictionary<string, object?> Obj(params (string Key, object? Value)[] kv) =>
        kv.ToDictionary(x => x.Key, x => x.Value);
}
