# Known issues and next steps

## Not implemented yet

| Feature | Notes |
|---|---|
| Item World innocents | Floors never have an innocent yet; needs `item_world/persuasion`, `use_bribe_item`, `innocent_dead` and innocent records |
| Character missions, daily requests (village board) | `trophy/character_missions`, `trophy/daily_requests` still get default answers |
| Equipping weapons / equipment on characters | |
| Item shop (`shop/buy_item`) | |
| Innocents | |
| Battle skip / auto repeat | |
| Arena, raids, events | Need a fake opponent / boss state |
| Hiding the AP bar | AP is never spent, but the bar still shows; could be done through `hook_j` |

## Known bugs

| Bug | Notes |
|---|---|
| Character detail shows the same sprite for every character | Not investigated yet |
| Retrofit screen crashes | NRE in `GetRetrofitNeedHl` |
| Potential screen crashes | NRE, not investigated yet |
