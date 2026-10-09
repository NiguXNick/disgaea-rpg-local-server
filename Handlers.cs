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
    private readonly MasterData _master;
    private readonly Shop _shop;
    private readonly Dictionary<string, Func<Player?, JsonObject, object?>> _map = new();

    public Handlers(GameTypes types, PlayerStore players)
    {
        _master = new MasterData(types);
        _chars = new Characters(_master);

        _map["player/add"] = (p, q) => Obj();
        _map["app/constants"] = (p, q) => null; // SyncDefineData defaults come from the class itself
        _map["battle/status"] = (p, q) => Nil;
        _map["battle/reset"] = (p, q) => Obj();

        _shop = new Shop(_master);
        _map["shop/index"] = _shop.Index;
        _map["shop/equipment_shop"] = _shop.EquipmentShop;
        _map["shop/equipment_items"] = _shop.EquipmentItems;
        _map["shop/change_equipment_items"] = _shop.Renew;
        _map["shop/buy_equipment"] = _shop.Buy;
        _map["shop/sell_equipment"] = _shop.Sell;
        _map["player/weapons"] = _shop.Weapons;
        _map["player/equipments"] = _shop.Equipments;

        var battle = new Battle(_master, _chars, Status, types);
        _map["player/sync"] = battle.Sync;
        _map["player/clear_stages"] = battle.ClearStages;
        _map["player/stage_missions"] = battle.StageMissions;
        _map["player/stage_currents"] = battle.StageCurrents;
        _map["player/items"] = battle.ItemList;
        _map["battle/help_list"] = battle.HelpList;
        _map["battle/start"] = (p, q) => battle.Start(p, q, "battle/start");
        _map["battle/end"] = (p, q) => battle.End(p, q, "battle/end");
        _map["battle/story"] = (p, q) => battle.Story(p, q, "battle/story");
        _map["player/abyss_gates"] = (p, q) => Obj(("t_abyss_gates", Array.Empty<object>()));

        var gacha = new Gacha(_master, _chars);
        _map["gacha/available"] = gacha.Available;
        _map["gacha/sums"] = gacha.Sums;
        _map["gacha/do"] = gacha.Do;
        _map["player/stone_sum"] = gacha.StoneSum;

        _map["player/tutorial"] = Tutorial;
        _map["player/tutorial_gacha_single"] = TutorialGachaSingle;
        _map["player/tutorial_choice_characters"] = (p, q) => Obj(("character_user_datas",
            TutorialChoices.Select(m => (object?)Characters.ToWire(_chars.Create(m, m, padSlots: true), p?.Id ?? 0)).ToList()));

        _map["player/profile"] = (p, q) => p == null ? null : Profile(p);
        _map["player/index"] = PlayerIndex;
        _map["player/characters"] = (p, q) => p?.Characters.Select(c => (object?)Characters.ToWire(c, p.Id)).ToList();
        _map["player/decks"] = Decks;
        _map["login/update"] = LoginUpdate;
        _map["player/deck_groups"] = DeckGroupList;
        _map["player/update_deck"] = UpdateDeck;

        // Sub tutorials (guided explanations on first visits) block their screens until the read
        // is recorded; offline every one is reported as already read, so none of them start.
        _map["player/sub_tutorials"] = (p, q) => p == null ? null : SubTutorialIds(p).Select(id => (object?)SubTutorialRow(p, id)).ToList();
        _map["sub_tutorial/read"] = (p, q) =>
        {
            if (p == null) return null;
            var id = U(q, "m_sub_tutorial_id");
            p.SubTutorialsRead.Add(id);
            return Obj(("after_t_sub_tutorials", new List<object?> { SubTutorialRow(p, id) }),
                ("after_t_items", Nil), ("after_t_status", Nil), ("after_t_agendas", Nil));
        };
        _map["arena/current"] = (p, q) => p == null ? null : Obj(("t_arena", Arena(p)));
        // Today's free bingo already drawn: the login bingo skips its lottery animation and the
        // home screen's popup chain continues (see MasterFix.AddMissingTables).
        _map["bingo/index"] = (p, q) => Obj(
            ("t_bingo_data", Obj(
                ("id", 1UL),
                ("date", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                ("last_lottery_at", Time.Format(DateTime.UtcNow)),
                ("drew_today", true),
                ("display_numbers", Array.Empty<object>()),
                ("bingo_indexes", Array.Empty<object>()),
                ("max_draw_count", 1))),
            ("rewards", Array.Empty<object>()),
            ("after_stone_sum", Nil));
        // One entry per owned character. PlayerManager.GetCharaMissionFromApi starts a loading
        // indicator and only clears it after one trophy/character_missions request per batch of
        // collection entries; with an empty collection no request is made and it never clears.
        _map["player/character_collections"] = (p, q) => p?.Characters
            .GroupBy(c => c.MCharacterId)
            .Select((g, i) => (object?)Obj(
                ("id", (ulong)(i + 1)),
                ("m_character_id", g.Key),
                ("max_lv", g.Max(c => c.Lv)),
                ("story_status", Array.Empty<object>())))
            .ToList();
        // No raid in progress. An empty object here makes GetRaid.RequestCurrent load raid 0 and
        // never finish, which keeps the home screen's loading indicator up.
        _map["raid/current"] = (p, q) => Obj(("current_t_raid_status", Nil));
        // RegularDataManager.UpdateAgendaBadge dereferences new_agenda without a null check.
        _map["player/badges"] = (p, q) => Obj(("new_agenda", Obj()));
    }

    // InnocentVillageUtility looks the player's kingdom rank up in MKingdomRank and crashes the
    // home screen (footer badges) when there's no matching row, e.g. rank 0.
    private int StartKingdomRank =>
        _master.All("MKingdomRank").Select(r => MasterData.F<int>(r, "kingdom_rank")).DefaultIfEmpty(1).Min();

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

    // During the tutorial command slots must all be filled (see Characters.Create); afterwards
    // repeated commands break the command screens, so duplicates are cleared.
    private void RepairCharacters(Player p)
    {
        foreach (var c in p.Characters)
        {
            if (p.IsTutorial)
            {
                if (c.Commands.Any(x => x == 0)) c.Commands = _chars.Create(c.Id, c.MCharacterId, c.Rarity, c.Lv, padSlots: true).Commands;
                continue;
            }
            var seen = new HashSet<ulong>();
            for (var i = 0; i < c.Commands.Length; i++)
                if (c.Commands[i] != 0 && !seen.Add(c.Commands[i])) c.Commands[i] = 0;
        }
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
                    var hero = _chars.Create(1, ids.FirstOrDefault(TutorialGachaPool[0]), p.GachaRarity > 0 ? p.GachaRarity : null, padSlots: true);
                    p.Characters = [hero];
                    p.Deck = [hero.Id];
                    break;
                case 7 when !gachaFix:
                    step = 6; // character selection screen
                    break;
                case 7:
                    foreach (var m in ids.Where(m => p.Character(m) == null))
                        p.Characters.Add(_chars.Create(m, m, padSlots: true));
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
            Log.Info($"Tutorial: step {step}{(p.IsTutorial ? "" : " (finished)")}");
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
        var now = Time.Format(DateTime.UtcNow);
        return Obj(
            ("player_adjusts", Array.Empty<object>()),
            ("profile", Profile(p)),
            ("record", Record(p)),
            ("player_setting", Obj(("id", p.Id), ("created_at", p.CreatedAt), ("updated_at", now))),
            ("status", Status(p)),
            ("act_give_count", Obj()),
            ("player_arena", Arena(p)));
    }

    // Arena BP is kept full: PlayerArenaBattleData.BpNow parses act_at every frame unless act is
    // at the cap, and a blank act_at (the default arena/current reply) throws on each frame.
    private static Dictionary<string, object?> Arena(Player p) => Obj(
        ("id", p.Id), ("m_arena_group_id", 0UL), ("battle_count", 0), ("act", 10),
        ("act_at", Time.Format(DateTime.UtcNow)),
        ("is_previous_reward_received", true), ("is_half_reward_received", true));

    // Every response that carries after_t_status replaces the client's whole status, so it must
    // always be the real one (a blank one resets kingdom_rank to 0 and breaks the home screen).
    private Dictionary<string, object?> Status(Player p)
    {
        var leader = p.Deck.Count > 0 ? p.Character(p.Deck[0]) : p.Characters.FirstOrDefault();
        var now = Time.Format(DateTime.UtcNow);
        return Obj(
            ("id", p.Id), ("t_player_id", p.Id), ("rank", p.Rank), ("exp", p.RankExp), ("exp_total", p.RankExpTotal),
            ("shop_rank", _shop.MaxShopRank), ("survey_rank", 1u),
            // Offline: AP is always full at the game's cap (stages cost 0 AP anyway, see MasterFix)
            // and boxes are at their maximum size (SyncDefineData *_space_max).
            ("act", 9999), ("act_max", 9999), ("act_at", now),
            ("character_max", 999), ("weapon_max", 999), ("equipment_max", 999), ("innocent_store_max", 999),
            ("deck_no", p.SelectedDeckNo), ("kingdom_rank", StartKingdomRank),
            ("favorite_char_id", leader?.Id ?? 0), ("favorite_m_char_id", leader?.MCharacterId ?? 0),
            ("agenda_confirm_at", now), ("last_free_gacha_at", ""), ("verify_age_date", ""));
    }

    private static Dictionary<string, object?> Record(Player p) => Obj(
        ("t_player_id", p.Id), ("play_day_num", 1), ("character_lv_max", 1),
        ("comeback_end_at", ""), ("last_healed_at", ""));

    private object? LoginUpdate(Player? p, JsonObject q)
    {
        if (p == null) return null;
        // Bonus lists must be nil when there's nothing to show: LoginBonusController skips a popup
        // only on null (an empty list opens the 8-day special login bonus window with no data and
        // locks the home screen under its dimmed background).
        return Obj(
            ("after_t_status", Status(p)),
            ("after_t_record", Record(p)),
            ("after_t_login_bonuses", Nil),
            ("after_present_count", 0),
            // Must be a list (even empty): HomeEngine only creates m_FinishedPassportIdList when it
            // isn't null, and PassportAttentionProcess dereferences that list unconditionally.
            ("after_t_passports", Array.Empty<object>()),
            ("after_t_campaign_login_bonuses", Nil),
            ("login_roulette_items", Nil),
            ("memorial_login_bonuses", Nil),
            ("help_reward", Nil),
            // Same for the "items were converted" popup (HomeEngine.ConvertItemPopupProcess).
            ("converted_item_data", Nil));
    }

    // SyncDefineData: deck_group_num groups of deck_max decks each.
    private const int DeckGroups = 10, DecksPerGroup = 7;

    // Every deck the party screen can flick to must exist: CharacterManager.GetDeckData falls back
    // to an empty DeckData whose null t_memory_ids crashes it, and PartyEditFlickController reads
    // memory slots 0..4 without bounds checks, so each deck carries five (empty) memory ids.
    private object? Decks(Player? p, JsonObject q)
    {
        if (p == null) return null;
        var decks = new List<object?>();
        for (var no = 1; no <= DeckGroups * DecksPerGroup; no++)
        {
            var saved = p.Decks.TryGetValue(no, out var d) ? d : no == p.SelectedDeckNo ? p.Deck : new List<ulong>();
            var ids = saved.Concat(Enumerable.Repeat(0UL, 5)).Take(5).ToArray();
            var leader = p.Character(ids[0]);
            decks.Add(Obj(("id", (ulong)no), ("t_player_id", p.Id), ("deck_no", no), ("name", p.DeckNames.GetValueOrDefault(no, "")),
                ("leader_t_character_id", ids[0]), ("leader_m_character_id", leader?.MCharacterId ?? 0),
                ("t_character_ids", Obj(("pos1", ids[0]), ("pos2", ids[1]), ("pos3", ids[2]), ("pos4", ids[3]), ("pos5", ids[4]))),
                ("t_memory_ids", new object[] { 0UL, 0UL, 0UL, 0UL, 0UL }),
                ("created_at", p.CreatedAt), ("updated_at", p.CreatedAt)));
        }
        return decks;
    }

    private const int SubTutorialStatusRead = 1;

    private IEnumerable<ulong> SubTutorialIds(Player p) =>
        _master.All("MSubTutorial").Select(r => MasterData.F<ulong>(r, "id")).Concat(p.SubTutorialsRead).Distinct();

    private static Dictionary<string, object?> SubTutorialRow(Player p, ulong id) => Obj(
        ("id", id), ("t_player_id", p.Id), ("m_sub_tutorial_id", id), ("status", SubTutorialStatusRead),
        ("updated_at", Time.Format(DateTime.UtcNow)));

    // deck_data: charaIdList[i] / t_memory_ids_list[i] are "id,id,id,id,id" for deck i+1, names[i].
    private static object? UpdateDeck(Player? p, JsonObject q)
    {
        if (p == null || q["deck_data"] is not JsonObject data) return null;
        var lists = data["charaIdList"] as JsonArray ?? [];
        var names = data["names"] as JsonArray ?? [];
        for (var i = 0; i < lists.Count; i++)
        {
            var ids = (lists[i]?.ToString() ?? "").Split(',')
                .Select(s => ulong.TryParse(s, out var v) && p.Character(v) != null ? v : 0UL)
                .Concat(Enumerable.Repeat(0UL, 5)).Take(5).ToList();
            if (ids.Any(x => x != 0) || p.Decks.ContainsKey(i + 1)) p.Decks[i + 1] = ids;
            var name = i < names.Count ? names[i]?.ToString() : null;
            if (!string.IsNullOrEmpty(name)) p.DeckNames[i + 1] = name;
        }
        p.SelectedDeckNo = I(data, "selectDeckNo", p.SelectedDeckNo);
        if (p.Decks.TryGetValue(p.SelectedDeckNo, out var selected) && selected.Any(x => x != 0))
            p.Deck = selected.Where(x => x != 0).ToList();
        Log.Info($"Party {p.SelectedDeckNo}: {string.Join(", ", p.Deck)}");
        return Obj();
    }

    private static object? DeckGroupList(Player? p, JsonObject q) =>
        p == null ? null : Enumerable.Range(1, DeckGroups)
            .Select(g => (object?)Obj(("id", (ulong)g), ("t_player_id", p.Id), ("deck_group_no", g), ("name", "")))
            .ToList();

    // Request parameter readers (prms is JsonUtility output; missing keys read as 0/false).
    internal static ulong U(JsonObject q, string key) => q[key] is JsonNode n && ulong.TryParse(n.ToString(), out var v) ? v : 0;
    internal static int I(JsonObject q, string key, int def = 0) => q[key] is JsonNode n && int.TryParse(n.ToString(), out var v) ? v : def;
    internal static long L(JsonObject q, string key) => q[key] is JsonNode n && long.TryParse(n.ToString(), out var v) ? v : 0;
    internal static bool B(JsonObject q, string key) => q[key] is JsonNode n && n.ToString() is "true" or "True" or "1";

    internal static Dictionary<string, object?> Obj(params (string Key, object? Value)[] kv) =>
        kv.ToDictionary(x => x.Key, x => x.Value);
}
