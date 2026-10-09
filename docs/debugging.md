# Debugging

## Logs

- **Server:** console output of `dotnet run`. `RPC not implemented (default response): <method>`
  shows what the client asked for that has no handler; `Field 'x' does not exist in T` means a
  handler sent a key the client class doesn't have.
- **Client:** `%USERPROFILE%\AppData\LocalLow\Boltrend\DISGAEA RPG\Player.log` (rewritten on every
  game start).

Many client exceptions (inside network callbacks and hotfix code) are only logged through
`XD.tool.Debug.LogException`, which is filtered by tag. Start the server with
`--client-log-all` to send `log=Log:ALL` in the server config: every tag is enabled and the
client also logs **every request and decoded response** (`DebugWebRequestLogger`), which is the
fastest way to see what the client actually received.

## Diagnostic command line options

| Option | Does |
|---|---|
| `--dump <method> [--prms <json>] [--as <account>]` | Runs one RPC and prints the response as JSON (use a throwaway account: it is modified) |
| `--master-fields <Type>` | Field order of a master record type |
| `--master-rows <table> <field> <value> [limit]` | Prints master rows where a field equals a value (`*` = every row; default limit 15) |
| `--master-test <file> <Type>` / `--master-test-all <dir>` | Reads master files with the game's own reader |
| `--master-fix <table>` / `--master-fix-all` | Runs the master upgrade (one table / all) |

In Windows PowerShell 5 escape quotes in `--prms`: `'{\"m_stage_id\":101102}'`.

## Checklist for a new endpoint

1. Find the connection class / call site and the result type (client-internals.md).
2. Read the callback: what it dereferences, which flags/loading counters it waits on.
3. Decide null vs empty vs filled for every field (serialization.md).
4. Implement, then `--dump` it and check there are no `Field … does not exist` warnings.
5. Test in game with `--client-log-all` and read `Player.log` for exceptions.

## Common symptoms

| Symptom | Usual cause |
|---|---|
| Infinite loading | A callback threw, or a flag/`LoadingStart` waits for a response that never comes (empty list → no request sent, id 0, stale date) |
| Grey dimmed screen that ignores taps | A popup opened with no data (empty list instead of null), or a popup chain step threw |
| Crash right after a response | A field the client dereferences was null, or a cache field was sent non-null |
| Empty data after restarting the server | The game was left open from before the restart (sessions are restored from the session id) |
