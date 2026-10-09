# Known issues and next steps

## Not implemented yet

| Feature | Notes |
|---|---|
| Gift box (presents) | Mission and event rewards normally arrive here |
| Missions (daily / weekly / story) | Another quartz source |
| Item World | Levelling weapons and equipment, innocents |
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
