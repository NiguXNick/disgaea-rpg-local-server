# Response serialisation

The client reads RPC results with `MsgPack.ObjectPacker` into its own C# classes. It is very
strict, and most bugs found so far came from small differences in how a response was shaped.
`SchemaWriter.cs` exists to get this right automatically: the server loads the client's
`Assembly-CSharp.dll` (`GameTypes.cs`) and writes every response as the exact C# type the client
expects (`methods.tsv` + the `ResponseData.result` field of each connection class give the type).

## What ObjectPacker requires

- **Unknown keys throw** (`FormatException` → reboot dialog). Keys are field names; auto-property
  backing fields use the property name. SchemaWriter drops unknown keys and logs a warning.
- **Field initialisers don't run** (`GetUninitializedObject`): a key that isn't sent is
  0 / null / false on the client.
- Integer widths: `int` must be fixint/int8/int16/int32/uint8/uint16/uint32 (never int64); `ulong`/`uint` must be
  unsigned; `bool` must be a msgpack bool; `double` must be float64; strings must be str.
- `List<T>` is **not** supported as an array: it is read as a normal object, i.e. its private
  fields `{_items: [...], _size: n}`. SchemaWriter writes lists that way.
- Enums, `Dictionary`, `DateTime`, interfaces and pointers are unsupported and omitted.

## Null vs empty vs filled: the main source of bugs

The client often treats "null" and "empty" differently. Here are the rules we learned while debugging:

1. **A non-null empty list frequently means "there is something to show".**
   - `login/update` bonus lists (`after_t_login_bonuses`, `login_roulette_items`,
     `memorial_login_bonuses`, `after_t_campaign_login_bonuses`, `converted_item_data`) must be
     **null** when there's nothing, or an empty popup opens and locks the home screen.
   - Tutorial `deck_character_ids` / `tutorial_characters` must be null for the default party.
2. **Some lists must be non-null even when empty**, because the client iterates them unchecked:
   `login/update.after_t_passports`, `gacha/available.ids/private_gachas`,
   `gacha/do.after_stone_sum`, `shop/sell_equipment.sell_equipments`.
3. **`after_*` fields are state updates applied as-is.** An empty object would wipe that state
   (an empty `after_t_status` reset the kingdom rank to 0). SchemaWriter leaves unset `after_*`
   objects null.
4. **Lazily built client caches must be null**, or the client thinks they're already built:
   `MasterEnemyData.t_character_commands`, `BattleStartResponseData.enemyDataList`,
   `m_OtherUserInnocentDatas`, `m_*` cache fields, master-record fields such as
   `CharacterUserData.m_character_data`. SchemaWriter keeps fields named `m_Xxx`/`_xxx` and
   `Master*`/`Boltrend*` typed fields null, and keeps null collections of real game objects null.
5. **Nested objects the client dereferences without checks** must be present. SchemaWriter fills
   unset nested game objects with empty objects (up to depth 4), except in the cases above.
6. **Optional objects whose presence changes behaviour** (raid, arena, tower, helper player…)
   must be null in battle responses; `Battle.NilOtherObjects` nils every object field not set.
7. Dates are `"yyyy-MM-dd HH:mm:ss"`; an empty date string that the client parses throws
   (`PlayerArenaBattleData.act_at` was parsed every frame).

Use `SchemaWriter.Nil` to send an explicit msgpack nil for a field (including arrays).

## Defaults

Fields a handler doesn't set are taken from a default instance of the class. Constructors are
only run for `SyncDefineData` (its initialisers are the game constants); other classes start
uninitialised because some constructors recurse forever.
