# HermesProxy for Turtle-WoW servers (Capycraft and friends)

This fork lets the **1.14.2.42597** classic client (as shipped by the Ravencraft/IchaLaunch package with its
custom content server) play on **Turtle-WoW-core servers** such as Capycraft, which run a 1.12.1 (build 5875)
world server behind a realmd that wants the Turtle client build **7272**. The core those servers run is
public as [tortoise-wow](https://github.com/tortoise-wow/tortoise-wow) (targets the Turtle 1.18.1.7272 client).

Branch `capycraft` carries all fixes; `master` is the earlier state.

## Setup

1. **Build** (needs the .NET SDK):
   ```
   dotnet publish HermesProxy/HermesProxy.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o <temp dir>
   ```
   Publish into a temporary folder and copy only `HermesProxy.exe` (+ `.pdb`) into the folder you run from:
   the publish output also contains the **stock** `CSV/` folder and a default `HermesProxy.config`, which would
   overwrite the Turtle data below.
2. **Data and config:** copy `deploy/CSV/` and `deploy/HermesProxy.config` next to the exe. `deploy/CSV` is the
   stock data plus Ravencraft's tables (see [Data](#data)). Set `ServerAddress` for your server.
3. **Client/launcher:** point the launcher's Hermes host at `127.0.0.1` (IchaLaunch/RavenLaunch:
   `raven114_hermes_host` / `raven114_local_hermes` in `settings.json`, host only, it appends `:1119`; edit while
   the launcher is closed). The launcher keeps serving the custom content (models) as usual.
4. After changing **item data** (CSV item tables, appearance data), delete the client's `_classic_era_\Cache`
   folder, or the client keeps the item records it already received.

## Config options added by this fork

| Option | Default | What it does |
|---|---|---|
| `LogonBuild` | 0 | Build sent only in the realmd logon challenge (Capycraft: `7272`). The world server still gets `ServerBuild` (5875). 0 = use `ServerBuild`. |
| `DropGameObjectEntriesAbove` | 0 | Drop GameObject creates with entry >= value. Capycraft: `1000000` (custom/event objects whose models the client lacks). |
| `DropNullDisplayGameObjects` | true | Drop GameObjects with no display id. |
| `DropGameObjectDisplays` | "" | Comma list of GameObject displays to drop (Capycraft config: `7087`, a transport; likely obsolete since the WMO fix). |
| `SkipCreatureEntries` | "" | Comma list of creature/GameObject entries to drop. |
| `AirSwimAsFlight` | true | Turtle "air swim" (server forces Swimming on our own player while airborne, e.g. the Caverns of Time drake) becomes real client flight; flight is reported to the server as swimming. |
| `ResolveUnknownVisibleItems` | true | Gear slots holding server-only item ids (transmog, e.g. `#171311`) are shown as a client item with the same display. |
| `CreatureDisplayFallback` | 3167 | Creature displays missing from the client's list are shown as this display (Stormwind City Guard); mounts get no model. 0 = off. |
| `LogCreatureCreates` | false | **Diagnostic:** `[CreateTrace]` line per created unit/object/player (display, gear, weapons) and `[SelfMove]` movement lines. |
| `DropAllGameObjects`, `DropAllUnits`, `DropZeroEntryGameObjects` | false | **Diagnostic** isolation switches. |

## Fixes (cause → fix)

All client crashes below are `ERROR #132 ACCESS_VIOLATION reading 0x10`, always at the same client code
site: the client resolving a model it doesn't have.

| Problem | Cause | Fix |
|---|---|---|
| Login rejected | Turtle realmd wants build 7272, world wants 5875 | `LogonBuild` |
| World handshake aborted | `SMSG_WARDEN_DATA` / `SMSG_CACHE_VERSION` during auth | allow-listed during the handshake (`WorldClient.cs`) |
| Crash entering Elwynn/Stormwind | custom GameObjects (entry >= 1000000) with models the client lacks | `DropGameObjectEntriesAbove=1000000`, `DropNullDisplayGameObjects` |
| Crash near specific NPCs (e.g. 80230, Lothos Riftwaker at MC) | creature displays missing from the client | `CreatureDisplayFallback` (logs `[DisplayFallback]`) |
| Crashes from custom items | proxy sent appearance records for items the client already has | `ClientItemAppearances` gate: no appearance hotfixes for client-known items |
| Custom items without models/icons | stock item data | Ravencraft's item tables in `deploy/CSV` |
| Crash near boats/zeppelins and WMO objects | transports only got their flags when their route was in `Transports1.csv`; WMO-model objects never got the map-object flag (0x100000), so the client loaded them as M2 | ported from Ravencraft's proxy: transports always flagged (unknown period → 300000), WMO displays (`GameObjectWmoDisplays1.csv`) / type 14 get 0x100000; Ravencraft's `Transports1.csv` (tuned periods, Turtle routes) |
| Stuck on the Caverns of Time drake | Turtle "air swim": server echoes our own movement with Swimming set; the 1.14 client can't swim in air | `AirSwimAsFlight` (SMSG_MOVE_SET_CAN_FLY on first echo, unset on dismount / non-swim echo / new world) |
| Transmogged gear invisible in the world | Capycraft sends transmogged slots as server-only item ids | `ResolveUnknownVisibleItems`: query the template, show a client item with the same display, re-send the slot. Queries made only for this push nothing to the client (pushing their appearance records crashed it). |
| "?" icons on Turtle items | the proxy's display → icon table only knows Blizzard displays | `deploy/CSV/ItemDisplayIdToFileDataId1.csv` + 10,833 rows from the Turtle client's `ItemDisplayInfo.dbc` (icon name → 1.14 icon via `tools/turtle/icons`); exact icon names only |
| Crash from transmog stand-ins | first item with the display could be a ring/consumable with no model | stand-ins limited to visible gear slots; items with an appearance row preferred (`BuildClientItemSnapshot`); NPC weapons use the same lookup |

## Data

`deploy/CSV` = stock HermesProxy CSVs plus, from Ravencraft's shipped proxy: `ClientItemAppearances1.csv`,
the item tables (`Item*`, `ItemSparse*`, `ItemAppearance*`, `ItemModifiedAppearance*`,
`ItemDisplayIdToFileDataId*`, `Hotfix/Item*`, `Hotfix/ItemDisplayInfo*`, …), `Transports1.csv` and
`GameObjectWmoDisplays1.csv`. Known gap: `ItemModifiedAppearance1.csv` is incomplete (e.g. vanilla starter
weapons are missing), so "has an appearance row" is a preference, not a requirement.

## Finding the next crash

1. The client writes `_classic_era_\Errors\<date> Crash - <pid>.txt`. If the first three stack addresses end in
   `…e0a ← …b69 ← …2ee`, it is the model-resolve crash.
2. Run with `LogCreatureCreates=true` and stdout redirected to a file. The proxy logs `[CreateTrace]` for every
   created object.
3. Correlate: which `(type, display)` is created right before each crash report (within ~1 s), and which ones were
   never seen in crash-free sessions. Display 7087, 3031 (transports) and 7010/18226 (creatures) were found this way.
4. `[ItemIcon]`, `[VisibleItem]`, `[DisplayFallback]`, `[AirSwim]` and `[SelfMove]` lines cover items, transmog,
   creature fallbacks and flight.

## Tools (`tools/turtle`)

- `dbscan/`: offline scan of the tortoise-wow world SQL (`sql/base` + `sql/database_updates/world`, applied in
  order) against the client lists in `CSV/`: creature displays missing from the client, custom-range
  GameObjects, items without model/icon, NPC weapons without a drawable stand-in. Results of the last run are in
  `docs/turtle-scan/`.
  ```
  dotnet run --project tools/turtle/dbscan -c Release -- <tortoise-wow>/sql <proxy>/CSV <dir with stock-*.txt logs> <out dir>
  ```
- `icons/`: `extract-dbc.ps1` pulls DBCs from a Turtle client's MPQs (needs a StormLib.dll), `map-icons.ps1`
  turns `ItemDisplayInfo.dbc` into icon rows for `ItemDisplayIdToFileDataId1.csv`.
- `raven-exe/`: read Ravencraft's single-file `HermesProxy.exe`: `extract-bundle.ps1` (HermesProxy.dll +
  Framework.dll, for decompiling with ilspycmd), `extract-all.ps1` + `make-fwdep.ps1` (run it unpacked on the
  installed runtime), `inspect-il.ps1` and `patcher/` (IL patching experiments).

## Known open issues

- **"?" icons (remaining):** Turtle-only icon art the 1.14 client lacks (e.g. Crest of Valor/Heroism
  `inv_misc_token_01/02`, 61000 Time-Worn Rune `inv_misc_rune_08_yellow`, some custom swords), plus displays with an
  empty icon name in Turtle's DBC. 70 of the scan's 297 no-icon items. Expected to come with Ravencraft content
  updates; same-family look-alikes were considered and left out on purpose.
- **Creatures shown as guards:** 57 displays (38 spawned, ~470 spawns; mostly Turtle's satyr remodels 21577–21601).
  If new launcher content adds those models, the proxy's `CreatureDisplayInfo.csv` must learn them, or the
  fallback keeps hiding them.
- **Unverified GameObjects:** 30 custom-range displays the proxy still sends (scan B). No list of the 1.14
  client's GameObject displays exists in this repo.
- **Gear looks without a stand-in** (some transmogs, 44 NPC weapons): no client item uses that display. A full
  1.14.2.42597 `ItemDisplayInfo` id list would show which displays the client has, so the proxy could create
  the appearance records itself.
- **Not ported from Ravencraft's proxy:** custom races (Goblin etc.: `CustomCharacterCustomizations.csv`,
  `InjectCustomRaceLanguageSkill`), character-name availability check, web credentials, re-sending own gear
  5 s after login (`ScheduleVisibleItemsRefresh`).

## Where to pull remaining fixes from

- **Ravencraft's proxy** (shipped in their client package as `HermesProxy\HermesProxy.exe`): extract with
  `tools/turtle/raven-exe`, decompile, diff against this tree (data files, packet handlers, methods).
- **tortoise-wow world SQL:** every NPC/object/item with its model ids; run `dbscan` after updates.
- **Client data lists for 1.14.2.42597** (ItemDisplayInfo, GameObjectDisplayInfo, CreatureDisplayInfo ids), e.g.
  from a public DB2 export such as wago.tools: turns the "unverified" scan results into verified ones.
- **Turtle 1.18.1.7272 client DBCs** (`ItemDisplayInfo.dbc` icons, `GameObjectDisplayInfo.dbc` models).
