# Implemented endpoints

Everything not listed gets a well-formed default response (`RPC not implemented` in the log).

## Login and tutorial (`Handlers.cs`)

| Method | Notes |
|---|---|
| `player/add` | `{}` |
| `app/constants` | `SyncDefineData` with its own initialisers |
| `player/tutorial` | Steps 0 prologue … 8 epilogue, 9 = finish (`is_tutorial=false`, step 11). Steps 2 and 7 only advance with `gacha_fix=true`; steps 4/6/8 carry `battle_result`. Resuming at 4/6/8 replays the battle. Tutorial characters get all 4 command slots filled (the tutorial result screen crashes on empty slots). |
| `player/tutorial_gacha_single`, `player/tutorial_choice_characters` | The choice list must contain m_character_ids 10001, 30001, 10009, 10014 |

## Player data and home (`Handlers.cs`)

| Method | Notes |
|---|---|
| `player/index`, `player/profile` | `profile.public_id` must be non-empty (otherwise the hotfix flips the client back into tutorial mode) |
| status (inside many responses) | rank/exp from the save, AP 9999/9999, box sizes 999, `kingdom_rank` = lowest MKingdomRank (0 crashes the footer), `shop_rank` = highest item rank |
| `login/update` | Real status; bonus lists null; `after_t_passports` empty list |
| `player/characters`, `player/character_collections` | One collection entry per owned character: with an empty collection `GetCharaMissionFromApi` never clears its loading indicator |
| `player/decks`, `player/deck_groups`, `player/update_deck` | All 70 decks (10 groups × 7), each with five `t_memory_ids` |
| `player/badges`, `raid/current`, `arena/current`, `bingo/index` | Minimal valid data (no raid, full arena BP, bingo already drawn today) |
| `player/sub_tutorials`, `sub_tutorial/read` | Every sub tutorial reported read (they block screens otherwise) |

## Summons (`Gacha.cs`)

| Method | Notes |
|---|---|
| `gacha/available` | MGacha ids in term, not tutorial, not ticket-only, with a rollable character |
| `gacha/sums` | Per-banner counters; `last_draw_at` without zero padding (the hotfix's `TimeZoneFix` strips `"00:00"`) |
| `gacha/do` | MGachaLot → MGachaGroupItem by rate, guaranteed group on the last multi-pull slot, never empty. Premium Summon (100001) can give any summonable character of the rolled rarity. Free quartz spent first. |
| `player/stone_sum` | Two rows: free (stone_type 1) and paid (2) |

## Stages and battles (`Battle.cs`)

| Method | Notes |
|---|---|
| `player/sync` | Lists `t_stages`/`t_stage_missions` with empty `updated_at` (forces a full fetch) |
| `player/clear_stages` | **Every MStage reported cleared** so all stages are selectable (the client unlocks a stage when its `appear_m_stage_id` is cleared). Real clears are kept in the save. |
| `player/stage_currents`, `player/stage_missions`, `player/abyss_gates`, `player/items` | |
| `battle/help_list` | One NPC helper (`t_player_id 0`): an empty list leaves nothing to tap |
| `battle/start` | Waves from MStageEnemyGroup (weighted by rate) + MEnemyGroupPosition; MEnemy rows; raid/arena/event fields null |
| `battle/end` | Character exp from defeated enemies (MCharacterLevel), rank exp = MStage.exp (MPlayerRank), HL ×5, quartz every win (5 × difficulty rank + stage exp / 10, +50 first clear, +10 per new mission star; stars read from the result JWT), drops: 30% per enemy from its MEnemy table + 25% bonus weapon/equipment per win |
| `battle/story` | Marks story stages cleared; `clear_areas`/`clear_episodes` must be null |

## Item World (`BattleItemWorld.cs`)

| Method | Notes |
|---|---|
| `item_world/start` | One floor per battle, floor = item `stage` + 1. `battle_type` must be 5, `m_stage_id` 0, `equipment_id`/`equipment_type` echoed, `stage` = floor (an MItemWorld id). The client has no Item World enemy table: waves are borrowed from a story stage whose `proper_level` is closest below item rank + floor (+20% on boss floors). No innocents yet (`t_innocent_id` 0). |
| `battle/end` (`battle_type` 5) | Item `stage` = floor, `lv` from MWeaponEquipmentLevel (boss floors jump: floor 30 = Lv50, 60 = Lv100, 100 = Lv180), stats recomputed. Depth by rarity value: 30 / 60 / 100 floors (<40 / <70 / legendary). `after_t_record` must be filled; `after_t_weapon` or `after_t_equipment` carries the item; `obey_innocent`, `remove_t_innocent`, `breeding_t_item`, `stage_mission_after`, `after_t_stage_current` must be null. Quartz: 2 + floor/5, +10 on boss floors. |

## Character growth (`Growth.cs`, `Characters.cs`)

| Method | Notes |
|---|---|
| `character/rebirth` | Allowed at the level cap. Cost from MNecessaryRebirthMaterial (row for MCharacter.character_type and the next rebirth number: HL + up to 7 materials). Level back to 1, cap +100 (`clamp(100 + rebirth_num * 100, 1, 9999)`), +100 mana. Keys are `after_character` (not `after_t_character`), `after_t_items` (never null) and `after_t_record` (never null, replaces the client's record). |
| Levels and stats | Levels already reached in an earlier life cost half the exp. Stats = `ceil((min + per_lv * (lv - 1)) * cap / 100)`: each reincarnation makes the character stronger at the same level. |
| `player/change_chara_command` | `m_command_ids` = 4 slots (0 empty); returns the full character. "Learned" = the rows of `t_character_commands` (never null; every equipped skill needs a row). Skills with `learn_type` 1 are learned on level up. |
| `player/update_command_new_off` | Echoes the character ("new" marks are not used offline) |

## Battle skip (`Battle.cs`)

| Method | Notes |
|---|---|
| `battle/skip` | `skip_num` wins at once, every enemy of the stage's waves defeated each run; same response as `battle/end` (`drop_result.drop_list` and `after_t_stage_current` never null). The button needs the stage's `skip_flg`, all 3 mission stars and a skip ticket: the item of `item_type` 24 whose `effect_value` lists the battle type (10002 for normal stages, 2401 for Dark Gates). Offline those tickets are kept at 999, like AP. |
| `battle/skip_parties` | `{skip_parties: []}` |

## Nether Pass (`Passport.cs`)

The Nether Pass is passport 1 (not a Boltrend subscription). It is active for everyone offline.

| Method | Notes |
|---|---|
| `passport/index` | `PassportData` list (`_items`/`_size`), unique ids. The client only checks `sum_days - now_days > 0`; 90 days left are kept constant (also blocks re-buying, limit 90). |
| `login/update` `after_t_passports` | The pass row only on the first call of a day, when MPassport 1's daily items (quartz ×60, Gate Key ×3, DG Skip Coupon ×12) go to the gift box; it opens the "today's pass reward" popup. Otherwise `[]` (never null). |
| `boltrend/common` (`get_points`) | `data` is a JSON string: `"[]"` |

Not used offline: `boltrend/subscriptions` (EXP subscription, Prinny/Master Pass; their BSubscription masters are missing), `boltrend/current_pass` / `receive_pass_items` (point Battle Pass; no BPass rows).

## UI hooks (`Hooks.cs`)

The server config points `hook_j` at the server (`hook_direct=true`): it serves the installed
`StreamingAssets/Data/hook_j` (XDCryptor: RC4 + gzip JSON) with extra entries appended, since the
served file replaces the whole set. An `XDDLL:XD.Hook.XDHookActive` entry on `header/act_win`
turns off the AP block of the top bar (value, gauge, timer, "+" button). Paths start at a
GameObject with `XDPluginHookRoot`; `setting_key` `XD_Const_True` is always on. No game file is
changed, and a failed download makes the client fall back to its own file.

## Equipping and gear presets (`Equipment.cs`)

| Method | Notes |
|---|---|
| `player/change_chara_equipment` | `set_weapon_id_list` [weapon], `set_equipment_id_list` [3 slots]; 0 = empty. The client knows who wears what only from each item's `set_chara_id`/`set_no` (weapon slot 0, equipment 0..2). Returns full rows of every item that changed in `t_weapons`/`t_equipments`; `t_characters` must be a list (never null). |
| `player/characters` (gear fields) | `weapons`/`equipments` (and effects/innocents) must be **null** on the player's own characters, otherwise equipped gear never shows; only guests (`t_player_id` 0) use the attached lists |
| `player/equipment_decks` | 10 presets, each with all five `position*` objects. Zero presets crashes the preset screen (it indexes them by a saved tab position, so the count must never shrink). Ids of sold items read as 0. |
| `weapon_equipment/update_equipment_deck` | Rename, explicit contents (four arrays of 5), copy a party's gear (`deck_no`) or one character's (`t_character_id` + `character_position`). `after_t_equipment_decks` must have at least one row. |
| `weapon_equipment/change_deck_equipments` | Applies a preset to a party; 0 keeps the current item |
| `player/weapon_effects`, `player/equipment_effects`, `weapon_equipment/update_effect_unconfirmed` | No alchemy effects yet: empty lists / an object with empty lists |

## Fishing Fleet (`Survey.cs`)

| Method | Notes |
|---|---|
| `survey/index` | One row per MSurvey area, always (`t_character_ids` never null). Without them the screen crashes reading the "fish condition" (`area_condition` 1..5, player data). State comes only from `end_at`: none = idle, future = sailing, past = back. Status `survey_rank` must match an MSurveyRank row. |
| `survey/start` | Trip length: offline one in-game hour takes 1 real minute |
| `survey/end` | Cancel or collect; `after_t_survey` never null. Catches from `MSurvey.presents` are applied directly (`drop_result.drop_list` never null), more with a better fish condition and Big / Super Big results. Characters exp on exp_type 1 areas. Any id left in `t_character_ids` counts as "on a trip" for the client even when idle, so the finished survey goes back empty and the crew comes back in `after_t_characters`. Fleet exp ranks the fleet up automatically (normally a Dark Assembly bill). |
| `survey/use_bribe_item` | Raises the fish condition by the item's effect value, up to 5. Must always succeed: the client has no error handler there. |

## Gift box (`Rewards.cs`)

Mission and trophy rewards are never applied by the client from the mission responses: the
server puts them in the gift box and applies them on `present/receive`.

| Method | Notes |
|---|---|
| `present/index` | Bare `GiftData` list (unreceived), `order` 0 asc / 1 desc, `conditions` filter (0 quartz, 1 characters, 2 gear, 3 HL, 4 AP, 99 other). With `is_limit_notice` (home expiry popup) return an empty list. `delete_at` must parse; gifts never expire offline. `reward_data` is a client cache: null. |
| `present/history` | Received gifts, newest first, 20 kept |
| `present/receive` | `received_ids` never null; `present_list`/`history_list` are the full new lists (null would empty them); `items`/`stones` absolute totals; `status` must stay null (an empty one wipes the player's) |

## Missions (`Missions.cs`, `Progress.cs`)

The client never computes progress; it shows the server's `now_num`/`status`. Handlers bump
counters in `Progress` (summons, battles, enemies, party edits, gifts, shop buys, HL/quartz spent,
Item World runs/floors, login days); everything else comes from the save (stage clears, mission
stars, levels, collections). Counters exist for the whole game, today and this week.

| Method | Notes |
|---|---|
| `trophy/beginner_missions` | 12 rows of a sheet: `sheet_type` 3 → sheet 31 (training), 4 → 41 (Item World), otherwise the player's `mission_sheet_no` (status field; 0 hides the home icon). `status` is a bool here. |
| `trophy/receive_beginner` | Reward to the gift box. When all 12 of a sheet are received: the MSheetReward goes to the gift box and `mission_sheet_no` moves on (returning a different number tells the client the sheet is done; `next_mission_datas` must be non-null). Training / Item World sheets set `*_mission_finished_at` instead. |
| `trophy/index`, `trophy/dailies`, `trophy/weeklies`, `trophy/repetitions` | `PlayerTrophyResponse<T>` (mapped by hand in `GameTypes`). Rows only for masters in term; trophies chain through `next_m_trophy_id` and the next one shows once the previous is received. Status 0 progress / 1 clear / 2 received. Repetition rows keep the progress left over after rewards. |
| `trophy/get_reward*` | `{id}` or `{receive_all:1}`; only the just-received rows come back (status 2), the other three arrays null |
| `player/badge_homes` | Gift and mission badge counts. `new_friend` must be an object. |

Offline policy: on the beginner sheets, conditions the server can't observe or features that
don't exist offline yet (friends, awakening, innocents, skills, level above 100…) count as done,
otherwise one impossible mission blocks the following sheets. On the trophy tabs they stay at 0.
Stages cost no AP, so "use AP" missions count 10 AP per won battle.

## Equipment shop (`Shop.cs`)

| Method | Notes |
|---|---|
| `shop/index`, `shop/equipment_shop` | `equipment_shop.id` ≠ 0 and `lineup_date` ≥ today, or the shop loads forever; `garapon_get_data` must be filled |
| `shop/equipment_items` | Daily lineup: 15 weapons + 15 equipment |
| `shop/change_equipment_items` | 5 free renewals a day, then 10 quartz |
| `shop/buy_equipment` | Client's price formula, HL deducted; `after_t_item` and `after_garapon_ticket` must be non-null |
| `shop/sell_equipment` | 30% of the price, echo `sell_equipments` |
| `player/weapons`, `player/equipments` | Owned items |
