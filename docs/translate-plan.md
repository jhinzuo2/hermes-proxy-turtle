# WoWTranslate for 1.14.2 (Capycraft via HermesProxy)

Status 2026-10-26: v1 built on branch `capycraft-translate`. It has been tested outside the game: live
translation calls, a proxy startup on spare ports, and the addon against a mocked WoW API. Not yet tested in-game.

## Goal

Live chat translation on the Ravencraft 1.14.2.42597 client while playing Capycraft
(1.12.1 server) through the Capycraft HermesProxy fork.

- **Incoming:** Chinese chat → English.
- **Outgoing:** you type English → it is sent in Chinese. Nothing marks it as translated.
- **Toggles:**
  - On-screen OUT button for outgoing.
  - Slash commands for macros.
  - Minimap button, which only opens the settings.
- **Settings window:**
  - Global on/off, incoming and outgoing on/off.
  - Per chat type and per joined channel, separately for incoming and outgoing.
  - Translate sender names, and show the original text.

Only CN↔EN. Once everything works the plan is to read English only, so "show original" stays off by
default.

### Never in scope

- Nameplates of any kind: ShaguPlates, pfUI/"patch-o", Plater, ThreatPlates, resizing, overhead names.

### Not in v1

- Tooltip name/guild translation, friends list, WIM-specific integration. WIM still gets translated whispers,
  because the proxy translates the packets themselves.
- Other languages, keyed or custom API providers, AFK pause.

## Where things live

| Part | Where |
|---|---|
| Repo / branch | `E:\Codex\HermesProxy-translate`, a git worktree of `E:\Codex\HermesProxy-stock\HermesProxy-master`, branch `capycraft-translate` (cut from `capycraft` dceb820) |
| Proxy module | `HermesProxy/Translation/` (+ small hooks in both `ChatHandler.cs`, `Settings.cs`, `GlobalSessionData.cs`, `Server.cs`) |
| Glossaries | `HermesProxy/Translation/Data/*.tsv`, embedded in the exe. Exported from the 1.12 addon's Lua glossaries. |
| Addon | `addon/WoWTranslate/` |
| Test proxy | `E:\Codex\1.14raven - Kopie\Ravencraft 1.14\HermesProxy-Translate\`: test exe, plus a one-time copy of the live `CSV\`, `HermesProxy.config` and `AccountData\`, with `Translation.Enabled=true` |
| Addon installed | `E:\Codex\1.14raven - Kopie\Ravencraft 1.14\_classic_era_\Interface\AddOns\WoWTranslate\` |

**Hands off what you play with now:** the `capycraft` branch, its checkout, and the live proxy folder
`E:\Codex\1.14raven\Ravencraft 1.14\HermesProxy-Stock` and its process are untouched.

Both proxies use 127.0.0.1:1119, so only one can run at a time. To test, close the game, stop the live proxy and
run `start-translate-proxy.bat` in the test folder (log: `translate-log.txt`). To go back, stop the test one and
start the live one as usual.

The module is off unless `Translation.Enabled=true`, so merging changes nothing for anyone who doesn't turn it on.

## Why the proxy does the translating

On 1.14 an addon may only send SAY, YELL and public channel messages during a key press or click, so the
1.12 "wait for the translation, then send" flow can't work there. Instead:
- Your message leaves the client at once and the proxy translates it on the way to the server.
- Incoming chat is translated before the client sees it. That's why Blizzard's chat frame, WIM and player links
  all just work.

A translated line arrives about 0.2–1 s late. Lines without Chinese, glossary hits and cache hits go through at once.
Chat keeps its order.

## How it works

### Proxy (`HermesProxy/Translation/`)

- `TranslationService`: the pipeline.
  1. Language check.
  2. Exact glossary hit.
  3. Chinese chat slang (`88` → bye, `10G` → 10g, ...).
  4. Markup protection: links, colours, textures and `{raid icons}` become `http://ph.wt/N` placeholders.
  5. Partial glossary.
  6. Cache, then the free backends.
  7. Restore the markup.

  EN→CN glossary terms become placeholders too. Otherwise Google returns English with Chinese pasted in, untranslated.
- `FreeTranslator`: gtx → tw-ob → dict-chrome-ex → edge-translate, ported from the 1.12 DLL. A backend that
  answers HTTP 429 is paused for 60 s. At most 4 requests at once. Uses .NET's own TLS, so no curl or `cacert.pem`.
- `TranslationCache`: in memory, plus `translation-cache.tsv` next to the exe.
- `TranslationSession` (one per connection):
  - The addon's settings.
  - Ordered queues to the client and to the server. On timeout or failure the original text goes through.
  - Your own echo: the server echoes what you sent in Chinese, and you see the English you typed (plus the
    Chinese when "show original" is on).
  - Translated outgoing text over 255 bytes is split without breaking item links.
- Config keys (`HermesProxy.config`): `Translation.Enabled` (false), `Translation.Backend` (auto),
  `Translation.TimeoutMs` (4000), `Translation.CacheFile` (translation-cache.tsv).

### Addon bridge

The addon whispers itself addon messages with prefix `WT`. The proxy answers them and never forwards them.
Requests start with `?`, answers with `!`:

| Request | Answer |
|---|---|
| `?HELLO` | `!HELLO|<version>|<backend>` |
| `?CFG|on=1;in=1;out=0;orig=0;it=sywprgobc;ot=...;xi=general;xo=` | `!CFG|ok` |
| `?NAME|<name>` | `!NAME|<name>|<english>` |
| `?STATUS` | `!STATUS|<text>` |
| `?CLEARCACHE` | `!CLEARCACHE|ok` |

Chat type codes: s say, y yell, w whisper, p party, r raid, g guild, o officer, b battleground, e emote,
c custom channels. `xi`/`xo` list channels to skip.

Until the addon sends `?CFG`, the defaults are: incoming on for all player chat, outgoing off.

### Addon (`addon/WoWTranslate/`, Interface 11402)

- `Core.lua`: settings (`WoWTranslateDB`), proxy handshake at every loading screen, slash commands.
- `Names.lua`: display-only sender name translation (wraps `GetColoredName`). The player link keeps the real name.
- `OutButton.lua`: OUT: ON/OFF button. Click to toggle, shift-drag to move.
- `Minimap.lua`: opens the settings, drag to move.
- `Config.lua`: the settings window.

Commands (all usable in macros):
- `/wt` opens the settings.
- `/wt on|off|toggle`, `/wt out on|off|toggle`, `/wt in on|off|toggle`.
- `/wt names on|off`, `/wt orig on|off`, `/wt button on|off`.
- `/wt status`, `/wt clearcache`, `/wt reconnect`.

## In-game test checklist

1. Start the test proxy (see above) and log in. Chat should say "connected to proxy translation". The minimap
   button and the OUT button should be there.
2. Chinese in World/Guild/Whisper shows as English.
3. With OUT on, type English in /say, /w, World: others see Chinese, and you see your English. Also try /say
   and /yell outdoors.
4. Item links in both directions are still clickable and correct.
5. `/wt out toggle` in a macro, and the OUT button, both switch outgoing. `/wt toggle` switches everything.
6. Sender names: the first message from a Chinese name shows it as is, later ones in English. Right-click → whisper
   still works.
7. Settings window: unticking a channel stops its translation right away.

## Later

- Merge `capycraft-translate` → `capycraft` once it has been played with, then update CAPYCRAFT-PROXY-NOTES (deploy) and
  the repo `deploy/` config.
- The wow-translate repo could get a README pointer to this.
