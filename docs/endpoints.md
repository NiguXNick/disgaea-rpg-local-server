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

## Equipment shop (`Shop.cs`)

| Method | Notes |
|---|---|
| `shop/index`, `shop/equipment_shop` | `equipment_shop.id` ≠ 0 and `lineup_date` ≥ today, or the shop loads forever; `garapon_get_data` must be filled |
| `shop/equipment_items` | Daily lineup: 15 weapons + 15 equipment |
| `shop/change_equipment_items` | 5 free renewals a day, then 10 quartz |
| `shop/buy_equipment` | Client's price formula, HL deducted; `after_t_item` and `after_garapon_ticket` must be non-null |
| `shop/sell_equipment` | 30% of the price, echo `sell_equipments` |
| `player/weapons`, `player/equipments` | Owned items |
