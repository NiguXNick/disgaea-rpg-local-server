# Netherworld Reborn

*The Netherworld never closes, dood!*

**Netherworld Reborn** is a local replacement server for the global Steam release of **DISGAEA RPG**, whose official
servers (Boltrend) shut down on 2023-05-12. With it the game boots again and can be played
offline, with progress saved on your own PC.

**Status: work in progress.** Working: boot, login, the full tutorial, the home screen and
summons (banners in term, single/10x pulls with the master rates). New players start with
3,000 free Nether Quartz. Story stages: every stage is open and costs no AP; battles give
character/rank exp, HL and quartz on every win (5 per difficulty rank plus a tenth of the
stage exp, +50 on the first clear, +10 per new mission star), enemy drops and bonus gear.
Also working: the Nether Pass, always active (its daily quartz, Gate Keys and skip coupons
arrive in the gift box), reincarnation (level cap above 100), swapping skills, battle skip, the AP bar
hidden from the top of the screen, the equipment shop, equipping gear and gear presets, the
Fishing Fleet, the gift box, missions (beginner sheets, trophies, daily,
weekly and repeatable ones; rewards arrive in the gift box) and the Item World (levelling
weapons and equipment floor by floor, without innocents for now). The rest of the game is still
being implemented, see [docs/todo.md](docs/todo.md).

This repository contains **no game files** (executables, DLLs, assets or master data) and no
encryption keys: the server and the scripts read what they need from your own installation.
You need the game installed through Steam.

**Only the global Steam release (client 3.2.10, published by Boltrend) is supported.** The
server and `setup-game.ps1` check the installed build and refuse to touch anything else. The
Japanese version is a different service and is not supported, and won't be.

## Requirements

- Windows, with DISGAEA RPG installed through Steam
- Steam running (offline mode is fine); the game quits at startup without it
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Usage

1. **Point the game at the local server** (once). In PowerShell, from the repository folder:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\setup-game.ps1
   ```
   If the game is not in the default Steam folder, add `-GameDir "D:\...\DISGAEA RPG"`.
   The original `config_server.ini` is kept as `config_server.ini.original`.

2. **Start the server** and keep it running while you play:
   ```powershell
   dotnet run
   ```
   Options: `--port 8765`, `--game "<game folder>"`, `--data "<save folder>"`.

3. **Launch the game from Steam.** On the login window type any account name: it only picks
   which save to use (the password is ignored). Fill in some password too, so the game
   remembers the account and fills the window in next time.

To undo everything: `powershell -ExecutionPolicy Bypass -File tools\restore-game.ps1`.

## How it works

- **Redirect:** `StreamingAssets/settings/*.ini` are gzip + RC4. `config_server.ini` is rewritten
  to point at `http://127.0.0.1:8765/Server`, from which the server hands out the server list and
  the server config (`api`, `asset`, `master_bin`, …).
- **Login:** the SDK config is served with `"status": "Disable"`, which makes the game itself
  replace the Boltrend web login with a simple account/password window (`/signin`).
- **Protocol:** AES-256-CBC + MessagePack requests (`/version_check`, `/signin`, and JSON-RPC over
  `/rpc`). The client deserialises responses very strictly (an unknown key is an error, integer
  widths matter, `List<T>` is read as `{_items, _size}`), so the server loads the
  `Assembly-CSharp.dll` **from your installation** via reflection and shapes every response exactly
  like the C# type the client expects. `methods.tsv` maps generic RPC methods to those types.
- **Master data:** the master data shipped with the install is about a year older than the 3.2.10
  client code (the official server always sent an up-to-date copy). On startup the server works out
  which fields each table is missing and rewrites the extracted files in
  `%USERPROFILE%\AppData\LocalLow\Boltrend\DISGAEA RPG\Boltrend\XDMaster` (originals kept as `*.bak`).
- **Saves:** one JSON file per account under `bin/.../save/players/`.

## Developer documentation

See [`docs/`](docs/README.md) for how the client works, the protocol, the serialisation rules,
master data, every implemented endpoint and how to debug.

## Layout

| File | Purpose |
|---|---|
| `BootFiles.cs` | List.ini, server config, SDK config, local asset CDN |
| `ApiRouter.cs` | encryption, `/version_check`, `/signin`, `/rpc` dispatch |
| `Handlers.cs` | RPC method logic (tutorial, player data, …) |
| `GameTypes.cs` / `SchemaWriter.cs` | client types and exact-shape serialisation |
| `MasterFix.cs` / `MasterData.cs` | master data upgrade and lookups |
| `Characters.cs`, `PlayerStore.cs` | characters and save files |
| `tools/` | scripts to redirect / restore the game |

RPC methods that are not implemented yet get a well-formed default response and show up in the
log as `RPC not implemented`.

## License

[GPL-3.0](LICENSE): you can use, modify and share the code, and forks must stay open source
under the same license. The license covers this repository's code only, not the game.

## Disclaimer

Non-commercial preservation project, not affiliated with Nippon Ichi Software, Forward Works or
Boltrend. DISGAEA is a trademark of its respective owners. Use only with a legitimate copy of the game.
