# Known issues and next steps

The client can call about 280 RPC methods; about 70 are implemented. Everything else gets a
well-formed default answer, which is enough for screens that only display data but not for
actions. Grouped by feature, roughly in order of how much they matter offline:

## Character growth

| Feature | RPCs | Notes |
|---|---|---|
| Skill levels, weapon mastery | `item/use_command_power_up`, `item/use_weapon_mastery_power_up` | Swapping skills works |
| Skills learned with mana | | Only level-learned skills are added |
| Super reincarnation, auto awakening | `character/super_rebirth`, `character/auto_awakening` | Normal reincarnation works |
| Awakening, Nether Enhancement | `player/awakening`, `character/retrofit` | Retrofit screen crashes (NRE in `GetRetrofitNeedHl`) |
| Mana, status up, potentials | `player/mana_assignment*`, `character/reset_mana`, `character/status_up`, `character/use_mana_potion`, `potential/*` | Potential screen crashes |
| Selling characters | `character/sells` | |

## Items, gear and innocents

| Feature | RPCs | Notes |
|---|---|---|
| Item shop and items | `shop/buy_item`, `item/use`, `item/sell`, `item/exchange_material` | |
| Gear effects, rarity up, remake | `player/weapon_effects`, `player/equipment_effects`, `weapon_equipment/*` | |
| Innocents | `player/innocents`, `innocent/*`, `item_world/persuasion`, `item_world/use_bribe_item`, `item_world/innocent_dead` | Item World floors never have an innocent yet |
| Fourth Dimension (Item World survey) | `item_world_survey/*` | |
| Innocent Village, breeding | `kingdom/*`, `breeding_center/*`, `trophy/daily_requests`, `trophy/get_reward_daily_request`, `trophy/change_daily_requests` | |

## Battles and modes

| Feature | RPCs | Notes |
|---|---|---|
| Dark Gate multi-skip | `battle/skip_stages` | Single-stage skip works |
| Dark Gates, Abyss Gates | `player/gates`, `item/use_gate`, `item/use_abyss_gate` | |
| Overlord's Tower, Ritual Training | `tower/start`, `player/tower`, `ritual_training/*` | |
| Dark Assembly | `agenda/*`, `player/agendas` | |
| Arena, Character Contest, Division | `arena/*`, `character_contest/*`, `division/*` | Need fake opponents and rankings |
| Raids, events | `raid/*`, `raid_boss/*`, `event/*` | Need a boss state and event masters in term |
| Memories, story extras | `memory/*`, `player/update_character_story` | |

## Other

| Feature | RPCs | Notes |
|---|---|---|
| Character missions | `trophy/character_missions`, `trophy/receive_character` | |
| Hospital, bingo prizes | `hospital/*`, `bingo/lottery`, `bingo/receive` | |
| Profile and settings | `player/update_name`, `player/update_comment`, `player/update_setting`, `player/update_home_customize` | |
| Friends | `friend/*` | Offline there are no other players |

Not needed offline: `boltrend/*` (store, subscriptions, passes), `inherit/*` (account transfer),
`system/version_*`, `adjust/add`, `questionnaire/*`, `webcast/*`, `player/update_device_token`.

## Known bugs

| Bug | Notes |
|---|---|
| Character detail shows the same sprite for every character | Not investigated yet |
| Retrofit screen crashes | NRE in `GetRetrofitNeedHl` |
| Potential screen crashes | NRE, not investigated yet |
