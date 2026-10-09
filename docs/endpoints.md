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
