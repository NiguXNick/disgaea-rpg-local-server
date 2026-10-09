# Client internals

The game is a Unity **Mono** build, so its C# can be decompiled (we used `ilspycmd`). Do not
commit decompiled code or game files to this repository.

## Assemblies

| File | Contents |
|---|---|
| `Managed/Assembly-CSharp.dll` | Game code. Almost every method starts with an `IL.DelegateBridge` check: if the hotfix registered a replacement, it runs instead. |
| `Managed/XDDLL.dll` | Boltrend/NetEase tooling: `ServerRedirect`, `Settings`, `UrlRequest`, `XDCryptor`, `SerializeBinary` |
| `Managed/XDSDK.dll` | SDK process (`SDKProcess`, status Release/Debug/Disable) |
| `Managed/ILSource.dll` | Only the ILRuntime interpreter |
| `StreamingAssets/Data/dydata` | **The real hotfix code** (see below) |

## The hotfix (`dydata`)

`dydata` = XDCryptor (RC4 + gzip) of `[int32 n][n bytes "XDRF" patch table][PE DLL "DyncDll.dll"]`.
Extract the DLL bytes after the patch table and decompile them to read the hotfix.

The XDRF table (`wxb.hotMgr.AutoReplaceV2`) binds hot methods to `__Hotfix_*` fields of game
types; other hot classes in `Hot.Generater` are bound by name. Class/method names are
obfuscated (`Hot.Generater.CYrj…`); identify them by signature (the first parameter `obj` is the
patched instance, `base_method` calls the original).

Things the hotfix changes that matter here: login path and body (`/signin`), version check
handling, `api`/`asset` settings, master loading (`MasterTool`), asset "downloaded" checks
(local StreamingAssets bundles count as downloaded), several home/shop/battle wrappers that
mostly add analytics.

## Finding what an RPC returns

- Connection classes (`Cloverlab.ConnectionDatas/*`, `ConnectionData/*`) declare `Method` and a
  nested `ResponseData` whose `result` field is the type. `GameTypes` maps these automatically.
- Generic calls (`PlayerManager.PlayerConnectionData(method, …)` + `GetData<ResponseData<T>>`)
  only reveal the type at the call site: `methods.tsv` lists them (extracted with a script
  that looks for the nearest `GetData<…>` after each method string).
- Then read the callback: which fields it dereferences, which loading flags it sets.

## Useful entry points

| Area | Classes |
|---|---|
| Title / login | `TitleEngine`, hotfix `CYrjkZQma65r0hQP7M`, `LoginConnector` |
| Tutorial | `TutorialManager`, `TutorialController*` |
| Home | `HomeEngine` (state machine: connections → setup → popup chain → idle) |
| Summons | `GachaEngine`, `GachaTopController`, `GachaController` |
| Stages / battles | `StageSelectEngine`, `AreaSelectController`, `BattleModeController`, `BattleController` |
| Equipment shop | `ShopEngine`, `EquipmentShopController`, `PanelBuyConfirm` |
| Loading indicator | `LoadingManager` (a counter: every `LoadingStart` needs a `LoadingStop`) |
