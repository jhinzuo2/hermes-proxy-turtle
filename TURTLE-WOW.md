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
| Invisible / static boats (Stormwind–Auberdine, Turtle transport 181646) | (1) display 7087 (all boats) was in `DropGameObjectDisplays` from crash hunting; (2) (2) suspected wrong periods in `Transports1.csv`. Capycraft sends every ship at (0,0,0) with state 1; that is fine, the client positions it from its taxi path (data0, Capycraft sends 294 = in the client) and the period | 7087 removed from the config; Periods: the SW ship was timed with `tools/turtle/boatclock` on a 1.18.1 client (cycle ~236 s: depart SW, ~28 s to the map switch, ~29 s to Darkshore, 59 s stop, back the same way) and against the proxy's create timers (map switches at ~1 s and ~116.5 s of a 234534 ms cycle, stable over 30 min), so Ravencraft's 234534 is right and the tortoise DB value 350818 is not. Verified 2026-10-27: a 1.14 crossing through the proxy hits departure, map switch and Darkshore arrival within 1 s of the 1.18.1 client. The other Turtle-only transports use the tortoise values (199313 Windrunner sailed fine), unmeasured. All three Auberdine ships visible and sailing (2026-10-27). `Transports1.csv` has an optional 3rd column `TimeOffsetMs` to shift the timer sent to the client (unused now; -40/-49 s only compensated the wrong period). Tried and not needed: `TransportPathRemap`, dock placement for (0,0,0) creates (made the ship jump to the pier), forcing state 1, template SpawnMap/AOI |
| Custom flight paths listed but clicking does nothing | the proxy routes a flight itself (direct path or shortest path over `TaxiPath/TaxiNodes/TaxiPathNode1.csv`) and silently gave up when it found none; those CSVs were stock (85 nodes), Turtle has 122 (custom 82, 83, 88–123) and 360 paths | `deploy/CSV` taxi tables from the Turtle client's DBCs (patch-9.mpq, `extract-dbc.ps1 -Files DBFilesClient\TaxiNodes.dbc,...`); placeholder paths (0 → 0) skipped; an unknown route now logs `No taxi route from node X to Y` |
| Items stuck at "Retrieving item information" (only a cache wipe helped) | a client DB query for an item already being fetched got an Invalid reply, which the client caches; and when the server's item equalled the client's no reply was sent at all | pending queries wait for the server's answer and always get a Valid reply (`HotfixHandler.cs`, `HandleItemQueryResponse`) |
| Inspect shows nothing for some gear (transmogged players, Capycraft's alternative tier set variants) | those slots carry server-only item ids (Capycraft's own items: tier variants with other stats, transmog copies); the client never asks for an Item row it doesn't know exists | `SendInspectItemRecords`: before the inspect result, send their Item/ItemSparse rows (no appearance records: those crashed the client). Name/stats right at once, icon shows after the frame redraws (e.g. switch tabs). Don't map them by name: variants share names |
| Turtle stat changes on client-known items not shown (e.g. 19402 Legguards of the Fallen Crusader) | runtime hotfix ids were sequential and reused for other records after each proxy restart, so the client kept stale cached data; and live pushes for records the client already has are ignored | ids derived from table+record+content (`ContentHotfixId`); after new item hotfixes the list is announced again (`ScheduleHotfixAnnounce`, at most every 15 s) and the client fetches them |
| Turtle enchant lines missing (head/leg/shoulder "+1..5% Haste" arcanums) | enchants 3016-3020 exist only in Turtle's SpellItemEnchantment; the 1.14 client has no record (Ravencraft added other custom enchants, not these) | `CSV/Hotfix/SpellItemEnchantment1.csv` (values from the Turtle client's DBC) sent as hotfixes in layout 2DF904D2 (WoWDBDefs, 1.14.0-1.14.3). Add more rows the same way; `[Enchant]` log lines show the ids on your own items |
| Crash from transmog stand-ins | first item with the display could be a ring/consumable with no model | stand-ins limited to visible gear slots; items with an appearance row preferred (`BuildClientItemSnapshot`); NPC weapons use the same lookup |
| Item stats/effects gone after a client cache wipe (e.g. The Heart of Dreams) | an item first fetched only to draw a gear slot got no Item/ItemSparse/ItemEffect records; later client DB queries were answered from that bare template | `EnsureItemRecords` before answering an Item/ItemSparse query from a cached template (logs `[EnsureItemRecords]`) |
| Turtle stat changes missing on other players' gear (e.g. 19372 Helm of Endless Rage) | the proxy only compares an item with the client's records when it gets the item from the server, and the client never asks for an item in its own data | `CheckClientItemStats`: each client-known item seen on a player is queried once; a difference becomes a hotfix (376 items in the first session) |
| Set bonuses missing (Turtle's alternative tier sets, custom crafted/PvP sets) or wrong | the 1.14 client knows 172 of Turtle's 359 item sets, and Turtle changed the bonuses of 92 of those (the bonus spells themselves are in Ravencraft's content) | all of Turtle's sets sent as `ItemSet`/`ItemSetSpell` hotfixes (`LoadItemSetHotfixes`, layouts 189535BF / 96588F97 from WoWDBDefs; data `CSV/Hotfix/ItemSet1.csv` + `ItemSetSpellClient1.csv` from `tools/turtle/item-sets.py`). A set's bonus rows take over the client's row ids for it, leftovers are removed |
| Hit chance shows 0% (character sheet, ECS, any era addon) | the client reads `ActivePlayer` `UiHitModifier`/`UiSpellHitModifier`; a vanilla server has neither and never sends the hidden passive auras that give hit (Turtle's own client doesn't know it either: BetterCharacterStats scans tooltips) | `HitDisplay.cs` adds it up like the server: equip spells of our gear, set bonuses we have the pieces for, known passive spells (talents), our auras; each spell's value from Turtle's Spell.dbc (`CSV/HitSpells1.csv`, `tools/turtle/hit-spells.py`). Display only; logs `[HitDisplay]`. Not counted: spell hit for a single school (e.g. Elemental Precision), hit from enchants (scopes) |
| Inspected players' gear has no enchants | the vanilla visible-item slot carries the permanent (sub-field 1) and temporary (2) enchant and the random property (8), but only the item id was passed on | `HandleInspectResult` adds them as `InspectEnchantData` / `RandomPropertiesID` |
| "Transmogrified to:" line flashes on own items, missing on other players | the client's port of Turtle_TransmogUI draws it only when a tooltip opens (its 0.2 s refresh drops it) and never for others; the 1.14 client ignores item modifiers for it | addon `HermesTransmog` (see [Addons](#addons)) redraws the line on every refresh. Names: own gear from Turtle's `TransmogStatus` (asked at login with `GetTransmogStatus` on `TW_TRANSMOG`), others from their Turtle UI's answer to `INSShowTransmogs`, which the server relays as `TW_CHAT_MSG_WHISPER\t\tINSTransmogs;slot;transmog;equipped` (two tabs) and the proxy forwards. Fallback: the proxy's guess from the look. Weapons are not in Turtle's inspect answer, so they keep the guess |
| Transmog to some looks invisible on others, fine on yourself (e.g. Checkmate transmogged to 55277 The End of All Ambitions, display 68106) | the display → client item map came only from the stock item CSVs, which miss the items Ravencraft added to the client, so those looks had no stand-in | `deploy/CSV/ClientItemLooks1.csv` (`tools/turtle/client-looks.py`): the client's own item → look rows (Item, ItemModifiedAppearance, ItemAppearance), kept only where the client's display id is the server's, used after the stock map. Server gear looks without a stand-in: 1,079 → 212 |
| Freezes of a few seconds every 15-40 s, client memory up to 5 GB, slow character screen and loading | the hotfix announce sent the table hash where the records carry the unique id, so the client never matched what it had and fetched the whole list (~6,000 records) on every re-announce, appending each copy to `Cache\ADB\enUS\DBCache.bin` (2 GB) | announce and records carry the same content hash (`HotfixRecord.ContentUniqueId`), so the client only fetches new or changed records; re-announce only for client-known items (others take the live pushes). Logs `[HotfixRequest] client asks for N of M`, `[HotfixAnnounce]`. A bloated `DBCache.bin` has to be deleted once (game closed) |
| Chat text centered until /reload | WoWTranslate's own chat fonts had no `justifyH`, so a re-applied font (login, window resize, new whisper tab) drew centered | WoWTranslate 2.5.3: every font left-justified, `SetJustifyH("LEFT")` after applying |

## Addons

Install into the 1.14 client's `_classic_era_\Interface\AddOns`.

- **`tools/turtle/HermesTransmog`**: keeps the "Transmogrified to:" line on item tooltips (own gear and others').
  Talks to the proxy with addon prefix `HPTM`, sent as a whisper to yourself; the proxy intercepts what the addon
  sends and answers the same way:
  - proxy → addon: `S|slot|equippedId|look` (own slot, the proxy's guess), `C|copyId|look` (a server-made transmog
    copy, id ≥ 100000), `T|player|slot|transmogId|equippedId` (an inspected player's Turtle UI answer, `start` first);
  - addon → proxy: `?` (resend after a reload), `I|player` (ask that player's Turtle UI, sent on `INSPECT_READY`).
  Also reads Turtle's own `TW_TRANSMOG` traffic. Names come from the server's item data, so they are right even for
  looks the client has no item for.
- **`addon/WoWTranslate`**: live CN↔EN chat translation with the proxy's translator (`HermesProxy/Translation`, prefix
  `WT`); plan and options in `docs/translate-plan.md`.

## Data

The fork's own data: `CSV/Hotfix/ItemSet1.csv` + `ItemSetSpellClient1.csv` (Turtle's item sets, see the fixes table),
`CSV/HitSpells1.csv` (spells that give hit), `CSV/Hotfix/SpellItemEnchantment1.csv` (Turtle-only enchants),
`CSV/ClientItemLooks1.csv` (the client's item → look rows where its display id is the server's). Turtle's
`ItemSet.dbc`/`Spell.dbc` came from the Turtle client's MPQs, last patch winning (`patch.mpq`, `patch-1`, `patch-2`,
`patch-Q`, `patch-X`, `patch-Y` on the Capycraft client).

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
   creature fallbacks and flight. `[TransmogLine]`, `[InspectTransmog]` and `[TurtleAddon]` (Turtle addon traffic
   both ways) cover the transmog line, `[HitDisplay]` the hit sent to the client, `[EnsureItemRecords]` item records
   made from a cached template, `[HotfixRequest]`/`[HotfixAnnounce]` how many hotfixes the client fetches and why,
   `[ItemMove]` every bag swap with the items on both sides (for bag sorting addons that never settle).

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
  1375801 (file id → path, e.g. icon names), ItemSet 1343609, ItemSetSpell 1314689, Spell 1140089 (`strings=0,1,2`),
  SpellName 1990283 (`strings=1`, Ravencraft's loose version, has Turtle's spells). The table hash it
  prints matches `DB2Hash.cs`. Compressed columns can come out wrong (ItemSetSpell's Threshold does); trust ids and
  relations.
  ```
  dotnet run --project tools/turtle/casc -c Release -- "<client dir>" table 1108759 CreatureDisplayInfo.csv
  ```
- `item-sets.py`, `hit-spells.py`: regenerate `ItemSet1.csv`/`ItemSetSpellClient1.csv` and `HitSpells1.csv` from
  Turtle's `ItemSet.dbc`/`Spell.dbc` (+ the client's ItemSetSpell dump); usage in each file's header.
  `client-looks.py` regenerates `ClientItemLooks1.csv` from the client's Item/ItemModifiedAppearance/ItemAppearance
  dumps and the tortoise-wow `item_template` (run it again after a Ravencraft content update).
- `HermesTransmog/`: the tooltip addon (see [Addons](#addons)). `boatclock/`: times transports on both clients.
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
- **Gear looks without a stand-in:** 212 server gear displays have no client item with that look (179 of them are
  in the client's `ItemDisplayInfo` under the same number, unverified that it is the same look; 33 are not in the
  client). Tracked per item in the GitHub issues.
- **Bag sorting (Bagnon) loops on a few items:** the server does every swap and the proxy passes the updates on;
  Bagnon keeps wanting the same pairs swapped. Probably the addon; `[ItemMove]` stays on.
- **Transmog line for weapons on others:** Turtle's inspect answer leaves weapon slots out, so they show the proxy's
  guess from the look (right for server-made copies, can name another item with the same look).
- **Moving to Xian55/HermesProxy:** a port of this branch onto Xian55's actively maintained fork is on local branch
  `capycraft-xian55` (not pushed, not tested in game; has everything up to the inspect enchants, without the
  2026-10-28 fixes: client item looks, hotfix unique ids, announce only for client items, `[ItemMove]`).
- **Not ported from Ravencraft's proxy:** character-name availability check, web credentials, re-sending own gear
  5 s after login (`ScheduleVisibleItemsRefresh`), its custom race files (`CustomCharacterCustomizations.csv`,
  `InjectCustomRaceLanguageSkill`). Not needed for races: Ravencraft's custom races are Turtle's (Goblin, High Elf)
  and play fine as is.

## Where to pull remaining fixes from

- **Ravencraft's proxy** (shipped in their client package as `HermesProxy\HermesProxy.exe`): extract with
  `tools/turtle/raven-exe`, decompile, diff against this tree (data files, packet handlers, methods).
- **tortoise-wow world SQL:** every NPC/object/item with its model ids; run `dbscan` after updates.
- **The client's own data** (`tools/turtle/casc`): after a launcher content update, re-dump CreatureDisplayInfo,
  ItemAppearance and ManifestInterfaceData and compare them with `CSV/`.
- **Turtle 1.18.1.7272 client DBCs** (`ItemDisplayInfo.dbc` icons, `GameObjectDisplayInfo.dbc` models).
