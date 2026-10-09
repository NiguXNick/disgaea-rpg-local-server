# Boot and login

## Settings files

`DISGAEA RPG_Data/StreamingAssets/settings/*.ini` are **gzip compressed, then RC4 encrypted**
with the key `<read from the installed game>` (`XD.tool.XDCryptor` / `FastCryptUtil` in `XDDLL.dll`).
`tools/XdCrypt.ps1` has `ConvertFrom-XdSettings` / `ConvertTo-XdSettings`.

A file `UnencrpytedStreamingAssetsSettings.txt` (misspelling as in the code) containing `false`
next to `DISGAEA RPG_Data` makes the client read **all** settings files as plain text. We don't
use it: `tools/setup-game.ps1` re-encrypts only `config_server.ini`.

Format (`XD.tool.Config.ReadText`): `key=value` lines, parsing **stops at the first blank line**,
a line without `=` throws and kills the loader. `{key}` placeholders are substituted.

## Boot sequence (`XD.Loader.Loader`)

1. `settings/local.ini`, then `config_server.ini` → `server_url`, `server_key`, `build`.
2. GET `{server_url}/List.ini` → list of server keys (`key`, `key=name` or `key@url`).
3. GET `{server_url}/{key}.ini` → the server config, merged into `Settings`.
4. `setting.ini`, hotfix code (`Data/dydata`), hooks (`Data/hook_j`), localisation, SDK config,
   analytics config. Remote URLs get `?timeStamp=` appended; failed requests are retried 4 times.
5. Scene 2 (title).

None of the loader failures stop the boot, but if steps 2 or 3 fail the SDK and analytics are
never created and login can't work.

## Server config keys (`BootFiles.cs`)

| Key | Use |
|---|---|
| `api` | Base URL of the API. Only scheme/host/port matter (the client replaces the path). Read by the hotfix `HotAppInitialize` into `ConstDefines.BaseUrl` / `HttpClient.HostUrl`. |
| `asset` | Asset CDN base; bundles are `{asset}/windows/...`. We serve `StreamingAssets/windows`. |
| `master_bin` | Must be non-empty or auto-login waits forever (`MasterTool.CompleteMasterDownload`). A 404 on `{master_bin}_ver` makes the client use the local master. |
| `time_zone` | Parsed with `Int32.Parse` without a null check. `0` = UTC. |
| `url_file`, `url_zip` | Hook downloads (news images); 404 is fine. |
| `config_sdk` | URL of the SDK config; loaded after the local one, so it wins. |
| `AutoSignin=false` | Don't start SDK sign-in automatically. |
| `log` | `Log:tag1,tag2`. `Log:ALL` turns every `XD.tool.Debug` tag on (see debugging.md). |

## Login without the Boltrend SDK

The Steam build logs in through the Steam overlay browser at Boltrend's web site, which is gone.
Serving the SDK config with `"status": "Disable"` (a **string**: the SDK config parser only
`Enum.Parse`s strings) makes the hotfix:

- show a built-in account/password window (`Prefabs/loc_login_win`) on "tap to start";
- send `POST /signin {uuid, password}` instead of `/steam/login`.

`/signin` must answer `{session_id, fuji_key, is_new}`:

- `is_new` must be a msgpack **bool** (the hotfix casts it).
- `fuji_key` becomes the AES key for later requests; we hand out the common key so one key
  covers everything.
- When `is_new` is true the client calls `player/add`.

The login window is pre-filled from `Boltrend/xdprefs` (`xd_account`, `xd_pwd`) **only when both
are non-empty**, so players should type some password.

Steam must be running: `SteamSDK.Init` quits the game otherwise.

Session ids are `<hex of account name>.<random>` so a game left open across a server restart is
still recognised (`PlayerStore.BySession`).
