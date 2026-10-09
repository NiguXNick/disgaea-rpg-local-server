# Netherworld Reborn: developer documentation

These documents explain how the DISGAEA RPG client talks to its server and how Netherworld
Reborn reproduces it, so anyone can continue the work.

| Document | What it covers |
|---|---|
| [boot-and-login.md](boot-and-login.md) | How the client finds its server, the settings files, login without the Boltrend SDK |
| [protocol.md](protocol.md) | Transport: AES + MessagePack, REST vs JSON-RPC, headers, errors |
| [serialization.md](serialization.md) | How responses must be shaped: the client's strict deserialiser and the null/empty rules that caused most bugs |
| [master-data.md](master-data.md) | The client's master tables, why the shipped ones are outdated, how the server upgrades and tweaks them |
| [client-internals.md](client-internals.md) | Hotfix code inside `dydata`, decompiling, where to look for things |
| [endpoints.md](endpoints.md) | Every implemented RPC: what it returns and the traps found for each |
| [debugging.md](debugging.md) | Logs, diagnostic command line options and a checklist for new endpoints |
| [todo.md](todo.md) | Known bugs and features still missing |

Code layout (repository root):

| File | Role |
|---|---|
| `Program.cs` | Startup, diagnostic modes, HTTP pipeline |
| `BootFiles.cs` | Server list / server config / SDK config / asset CDN routes |
| `ApiRouter.cs` | Decryption, `/version_check`, `/signin`, `/rpc` dispatch |
| `Handlers.cs` | RPC registry and smaller handlers (tutorial, home, decks, sub tutorials…) |
| `Gacha.cs`, `Battle.cs`, `Shop.cs` | Summons, story battles and drops, equipment shop |
| `Characters.cs` | Character creation, commands, exp/levels |
| `PlayerStore.cs` | Save files and sessions |
| `GameTypes.cs` | Loads the client's `Assembly-CSharp.dll`, maps RPC methods to result types |
| `SchemaWriter.cs` | Serialises responses exactly like the client's types |
| `MasterData.cs`, `MasterFix.cs` | Reads master tables; upgrades/tweaks them on disk |
| `methods.tsv` | RPC method → result type, extracted from the decompiled client |
