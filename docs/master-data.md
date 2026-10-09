# Master data

Master data (stages, enemies, characters, gachas, items…) ships in
`StreamingAssets/Data/master`, a password-protected zip. At login the hotfix `MasterTool` (which
holds the password) extracts it to `%USERPROFILE%\AppData\LocalLow\Boltrend\DISGAEA RPG\Boltrend\XDMaster`
(only if `XDMaster/flist` doesn't exist) and loads every file listed in `flist`.

## Table file format

`<Table>_<n>.bin`, read by `XD.Serialize.SerializeBinary`:

- int32 record count, then each record's `[SerializeField]` fields in
  `ReflectionTool.GetFieldWithAttribute` order (base class fields first, declaration order);
- strings are .NET `BinaryWriter` strings (7-bit length); `"null_s"` means empty;
- arrays are an int32 count (`-1` = null) followed by the elements;
- `"#EOF"` string at the end.

File prefix → type comes from `MasterDataManager.TableNameDic` (`MArea` → `MasterAreaData`);
Boltrend tables use `B` (`BProduct` → `BoltrendProductData`).

## Why the server rewrites it

The shipped master is about a year older than the 3.2.10 client code: many tables lack fields
added later, so the client's reader hits end-of-stream and `MasterLoadFile.LoadAll` dies (the
title screen then waits forever). The live server always sent an up-to-date master via
`master_bin`.

`MasterFix.cs` repairs the extracted files on every server start (originals kept as `*.bak`):

1. For each table the game's own reader rejects, search the smallest set of edits that makes
   the old file parse exactly: first fields appended at the end of the class (most common), then
   one missing field anywhere, then a tail plus up to two in the middle, then 2 or 3 arbitrary
   missing/obsolete fields. All files of a table must be explained by the same layout, and string
   fields with name-implied formats (`*_at` dates, `bgm`) must look right. Both rules remove most
   ambiguity.
2. Three tables were analysed by hand (`KnownMissing`): `MStage`, `MCommand`, `MEnemy`.
3. New fields get defaults; new `int[]` fields get the length of their neighbouring array
   (they run parallel, e.g. `MCommand.trigger_types` per effect).

`--master-test-all <dir>` checks every table with the game's reader.

## Offline tweaks (also in `MasterFix.cs`)

| Change | Why |
|---|---|
| Add `MBingoGroup_1.bin` (one always-open group) and list it in `flist` | With no bingo group the home screen's login bingo crashes and its popup chain stops |
| `MGacha.price_type 3 → 2` | Paid-only quartz banners accept free quartz |
| `MGacha.close_at → 2099` (non-tutorial) | Every expired banner is available |
| `MStage.act → 0` | Stages cost no AP |

`tools/restore-game.ps1` restores the `.bak` originals.
