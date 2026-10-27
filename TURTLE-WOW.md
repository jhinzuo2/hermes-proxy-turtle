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
| `DropGameObjectEntriesAbove` | 0 | Drop GameObject creates with entry >= value. Capycraft: `0` (was `1000000`; not needed since the WMO/transport fix, see below). |
| `DropNullDisplayGameObjects` | true | Drop GameObjects with no display id. |
| `DropGameObjectDisplays` | "" | Comma list of GameObject displays to drop (Capycraft config: empty since 2026-10-26; it held `7087`, the boat model, from before the WMO/transport fix, which made every boat incl. Stormwind–Auberdine invisible). |
| `TransportPathRemap` | "" | `from:to` list: transport taxi path the server sends → the client's path for the same route. Capycraft: empty (it already sends the client's path ids); logs `[Transport] entry=… path=… -> …`. |
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
| Crash entering Elwynn/Stormwind | first worked around by dropping custom GameObjects (entry >= 1000000); the real cause was the missing WMO/transport flags (row below). Ravencraft's client content defines Turtle's object displays, and Ravencraft's proxy drops nothing | `DropNullDisplayGameObjects`; `DropGameObjectEntriesAbove=0` since 2026-10-25 (custom props like Stormwind's food cart, barrels, crates are back, no crashes so far) |
| Crash near specific NPCs (e.g. 80230 High Elf Ranger, display 18226) | creature displays missing from the client | `CreatureDisplayFallback` (logs `[DisplayFallback]`); 80230 no longer skipped. Since 2026-10-26 `CreatureDisplayInfo.csv` also holds the 95 displays the client's own table has and the proxy's lacked (incl. 18226, 7010 and the satyrs 21577–21601), read with `tools/turtle/casc`; the fallback now only covers displays the client really lacks |
| Crashes from custom items | proxy sent appearance records for items the client already has | `ClientItemAppearances` gate: no appearance hotfixes for client-known items |
| Custom items without models/icons | stock item data | Ravencraft's item tables in `deploy/CSV` |
| Crash near boats/zeppelins and WMO objects | transports only got their flags when their route was in `Transports1.csv`; WMO-model objects never got the map-object flag (0x100000), so the client loaded them as M2 | ported from Ravencraft's proxy: transports always flagged (unknown period → 300000), WMO displays (`GameObjectWmoDisplays1.csv`) / type 14 get 0x100000; Ravencraft's `Transports1.csv` (tuned periods, Turtle routes) |
| Stuck on the Caverns of Time drake | Turtle "air swim": server echoes our own movement with Swimming set; the 1.14 client can't swim in air | `AirSwimAsFlight` (SMSG_MOVE_SET_CAN_FLY on first echo, unset on dismount / non-swim echo / new world) |
| Transmogged gear invisible in the world | Capycraft sends transmogged slots as server-only item ids | `ResolveUnknownVisibleItems`: query the template, show a client item with the same display, re-send the slot. Queries made only for this push nothing to the client (pushing their appearance records crashed it). |
| "?" icons on Turtle items | the proxy's display → icon table only knows Blizzard displays | `deploy/CSV/ItemDisplayIdToFileDataId1.csv` + rows from the Turtle client's `ItemDisplayInfo.dbc` (icon name → 1.14 icon via `tools/turtle/client-dbc`). Checked 2026-10-26 against the 1.14 client itself (`tools/turtle/casc`): 69 rows fixed to the icon with the exact name (the majority vote had picked another), 1,436 dropped because their icon file (mostly TBC art) is not in the client, 5 added; 9,402 Turtle rows remain |
| Invisible / static boats (Stormwind–Auberdine, Turtle transport 181646) | (1) display 7087 (all boats) was in `DropGameObjectDisplays` from crash hunting; (2) `Transports1.csv` had Ravencraft's period for Turtle's own transports (SW ship 234534 instead of 350818), so the client placed the ship at the wrong point of its route (often on the other continent). Capycraft sends every ship at (0,0,0) with state 1; that is fine, the client positions it from its taxi path (data0, Capycraft sends 294 = in the client) and the period | 7087 removed from the config; periods of the Turtle-only transports (181646, 176250, 190549, 190550, 190552) from the tortoise `transports` table. All three Auberdine ships visible and sailing (2026-10-27); SW ship departs ~10 s early, under observation. Tried and not needed: `TransportPathRemap`, dock placement for (0,0,0) creates (`Transports1.csv` Path column, made the ship jump to the pier), forcing state 1, template SpawnMap/AOI |
| Custom flight paths listed but clicking does nothing | the proxy routes a flight itself (direct path or shortest path over `TaxiPath/TaxiNodes/TaxiPathNode1.csv`) and silently gave up when it found none; those CSVs were stock (85 nodes), Turtle has 122 (custom 82, 83, 88–123) and 360 paths | `deploy/CSV` taxi tables from the Turtle client's DBCs (patch-9.mpq, `extract-dbc.ps1 -Files DBFilesClient\TaxiNodes.dbc,...`); placeholder paths (0 → 0) skipped; an unknown route now logs `No taxi route from node X to Y` |
| Crash from transmog stand-ins | first item with the display could be a ring/consumable with no model | stand-ins limited to visible gear slots; items with an appearance row preferred (`BuildClientItemSnapshot`); NPC weapons use the same lookup |

## Data

`deploy/CSV` = stock HermesProxy CSVs plus, from Ravencraft's shipped proxy: `ClientItemAppearances1.csv`,
the item tables (`Item*`, `ItemSparse*`, `ItemAppearance*`, `ItemModifiedAppearance*`,
`ItemDisplayIdToFileDataId*`, `Hotfix/Item*`, `Hotfix/ItemDisplayInfo*`, …), `Transports1.csv` and
`GameObjectWmoDisplays1.csv`, and from the Turtle client's DBCs: `TaxiNodes1.csv`, `TaxiPath1.csv`, `TaxiPathNode1.csv`. Known gap: `ItemModifiedAppearance1.csv` is incomplete (e.g. vanilla starter
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
  A 5th argument (a creature name, e.g. `"Lisa Jenkins"`) instead lists every object and NPC spawned within 25
  yards of it, with entry, display and type: handy for "something is missing next to X".
- `client-dbc/`: `extract-dbc.ps1` pulls DBCs from a Turtle client's MPQs (needs a StormLib.dll; `patch.MPQ` alone
  holds the 1.12.1 tables), `map-icons.ps1` turns `ItemDisplayInfo.dbc` into icon rows for
  `ItemDisplayIdToFileDataId1.csv`, `map-go-displays.ps1` lists Turtle GameObject displays whose model file a 1.12
  display also uses (1,613 of them; unused so far, a fallback if custom objects ever have to be hidden again).
- `casc/`: reads the 1.14.2 client's own data (CASC: local `Data/data` archives plus the launcher's loose files under
  `cdn/`, which hold Ravencraft's custom content), no CascLib needed. `table <fdid> <out.csv>` dumps a DB2 (WDC3,
  integer columns, strings where they point into the string table), `file`, `ids` (every file id the client has),
  `loose`, `find <hash,hash> [from] [to]` (file ids of the DB2s with those table hashes), and `dbc <file.dbc> <out.csv>`  for 1.12 DBCs. Foreign keys from the relationship map come out as the last column `rel`. Useful file ids: Item 841626, ItemAppearance 982462,
  ItemModifiedAppearance 982457, ItemDisplayInfo 1266429, CreatureDisplayInfo 1108759, CreatureModelData 1365368,
  GameObjectDisplayInfo 1266277 (model file id = column `f2`), GameObjects 841620, TransportAnimation 1140686,
  TaxiPathNode 1000437 / TaxiPath 1067802 / TaxiNodes 1068100 (Ravencraft's loose Turtle versions), ManifestInterfaceData
  1375801 (file id → path, e.g. icon names). The table hash it
  prints matches `DB2Hash.cs`.
  ```
  dotnet run --project tools/turtle/casc -c Release -- "<client dir>" table 1108759 CreatureDisplayInfo.csv
  ```
- `raven-exe/`: read Ravencraft's single-file `HermesProxy.exe`: `extract-bundle.ps1` (HermesProxy.dll +
  Framework.dll, for decompiling with ilspycmd), `extract-all.ps1` + `make-fwdep.ps1` (run it unpacked on the
  installed runtime), `inspect-il.ps1` and `patcher/` (IL patching experiments).

## Known open issues

- **"?" icons (remaining):** Turtle-only icon art the 1.14 client lacks. Checked in the client itself: Crest of
  Valor/Heroism (displays 69120/69119, `inv_misc_token_02/01`), 61000 Time-Worn Rune (`inv_misc_rune_08_yellow`) and
  Eye of Dormant Corruption (69123, `inv_misc_eye_03`) are not client items, have no client appearance and no icon
  file of that name, so there is no exact icon to point at. Ravencraft's content does carry its own icons for other
  Turtle displays (file ids 9100xxxx, used by its `ItemAppearance`); a later content update may add these.
  Same-family look-alikes were considered and left out on purpose.
- **Creatures shown as guards:** the 57 displays from the scan are in the client's table with their model files
  (Ravencraft content), so they are now in `CreatureDisplayInfo.csv` and shown as themselves. If one of them crashes
  (`ERROR #132` site above), remove its row: the fallback takes over again.
- **Unverified GameObjects:** with `DropGameObjectEntriesAbove=0` every custom object is sent; Stormwind and the
  food-cart area are fine. 27 of the 30 custom displays are in the client's `GameObjectDisplayInfo`; 12 of those point
  at a model file the client lacks, and two of them (5494, 24081) were created in play without a crash (nothing
  drawn). If one crashes, find it via `[CreateTrace]` and add its display to `DropGameObjectDisplays` rather than
  bringing the entry rule back.
- **Guild bank icons look grey** (still usable): the proxy's guild bank packets match Ravencraft's exactly
  (`Locked` never set), so it's on the client/UI side. Unchecked: same on Ravencraft's own server? with
  `Bagnon_GuildBank` off?
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
- **The client's own data** (`tools/turtle/casc`): after a launcher content update, re-dump CreatureDisplayInfo,
  ItemAppearance and ManifestInterfaceData and compare them with `CSV/`.
- **Turtle 1.18.1.7272 client DBCs** (`ItemDisplayInfo.dbc` icons, `GameObjectDisplayInfo.dbc` models).
