-- WoWTranslate 2 (1.14 client): settings, slash commands and the link to the proxy.
--
-- The translating itself happens in HermesProxy (Translation.Enabled=true in its
-- HermesProxy.config). This addon tells the proxy what to translate and shows the
-- controls. They talk through addon messages with prefix "WT" that the addon whispers
-- to itself; the proxy answers them and never passes them on to the server.

local ADDON, WT = ...
WoWTranslate = WT

WT.PREFIX = "WT"

-- Chat type codes shared with the proxy (TranslationSession.TypeGroups).
WT.CHAT_TYPES = {
    { code = "s", label = "Say" },
    { code = "y", label = "Yell" },
    { code = "w", label = "Whisper" },
    { code = "p", label = "Party" },
    { code = "r", label = "Raid" },
    { code = "g", label = "Guild" },
    { code = "o", label = "Officer" },
    { code = "b", label = "Battleground" },
    { code = "e", label = "Emote" },
    { code = "c", label = "Channels (World etc.)" },
}

local DEFAULTS = {
    enabled = true,
    incoming = true,
    outgoing = false,
    translateNames = true,
    nameStyle = "english",  -- "english" = Translated, "both" = Original | Translated
    translateChannels = true,
    unifyChatFont = true,    -- same font/size for Chinese and English in chat (ChatFont.lua)
    showOriginalIn = false,  -- incoming: "English (中文)"
    showOriginalOut = false, -- your own messages: "what you typed (what was sent)"
    showOutButton = true,
    inTypes = { s = true, y = true, w = true, p = true, r = true, g = true, o = true, b = true, e = false, c = true },
    outTypes = { s = true, y = true, w = true, p = true, r = true, g = true, o = true, b = true, e = false, c = true },
    inChannelsOff = {},  -- lower-case channel name -> true
    outChannelsOff = {},
    outButton = nil,     -- { point, relativePoint, x, y }
    minimap = { angle = 215, hide = false },
    names = {},          -- Chinese name -> English
}

local function CopyDefaults(src, dst)
    for k, v in pairs(src) do
        if type(v) == "table" then
            if type(dst[k]) ~= "table" then dst[k] = {} end
            CopyDefaults(v, dst[k])
        elseif dst[k] == nil then
            dst[k] = v
        end
    end
end

WT.proxyVersion = nil  -- set once the proxy answers ?HELLO
WT.proxyBackend = nil
WT.lastStatus = nil

function WT.Print(msg)
    DEFAULT_CHAT_FRAME:AddMessage("|cff33ccff[WoWTranslate]|r " .. tostring(msg))
end

-- ---------------------------------------------------------------------------
-- Proxy link
-- ---------------------------------------------------------------------------

function WT.Send(msg)
    local me = UnitName("player")
    if not me then return end
    C_ChatInfo.SendAddonMessage(WT.PREFIX, msg, "WHISPER", me)
end

local function CodesFrom(set)
    local out = ""
    for _, t in ipairs(WT.CHAT_TYPES) do
        if set[t.code] then out = out .. t.code end
    end
    return out
end

local function NamesFrom(set)
    local list = {}
    for name, off in pairs(set) do
        if off then table.insert(list, (name:gsub("[;,|]", ""))) end
    end
    table.sort(list)
    return table.concat(list, ",")
end

local function Bool(v) return v and "1" or "0" end

-- Pushes the current settings to the proxy. Called on login and whenever one changes.
function WT.PushConfig()
    local db = WoWTranslateDB
    local cfg = "?CFG|on=" .. Bool(db.enabled) ..
        ";in=" .. Bool(db.incoming) ..
        ";out=" .. Bool(db.outgoing) ..
        ";oi=" .. Bool(db.showOriginalIn) ..
        ";oo=" .. Bool(db.showOriginalOut) ..
        ";it=" .. CodesFrom(db.inTypes) ..
        ";ot=" .. CodesFrom(db.outTypes) ..
        ";xi=" .. NamesFrom(db.inChannelsOff) ..
        ";xo=" .. NamesFrom(db.outChannelsOff)
    if #cfg > 255 then
        cfg = cfg:sub(1, 255) -- channel lists are last; a cut only loses some skipped channels
    end
    WT.Send(cfg)
end

function WT.Hello()
    WT.Send("?HELLO")
    C_Timer.After(5, function()
        if not WT.proxyVersion then
            WT.UpdateStatusText()
        end
    end)
end

function WT.StatusText()
    if not WT.proxyVersion then
        return "|cffff5555Proxy translation not active|r (needs the Capycraft HermesProxy with Translation.Enabled=true)"
    end
    local db = WoWTranslateDB
    local state = db.enabled and "|cff55ff55ON|r" or "|cffff5555OFF|r"
    return "Proxy connected (backend " .. (WT.proxyBackend or "?") .. ") - translation " .. state ..
        ", incoming " .. (db.incoming and "on" or "off") .. ", outgoing " .. (db.outgoing and "on" or "off")
end

function WT.UpdateStatusText()
    if WT.OnStateChanged then WT.OnStateChanged() end
end

local handlers = {}

handlers.HELLO = function(version, backend)
    local first = not WT.proxyVersion
    WT.proxyVersion = version
    WT.proxyBackend = backend
    WT.PushConfig()
    if first then
        WT.Print("connected to proxy translation (backend " .. tostring(backend) .. "). /wt for options.")
    end
    WT.UpdateStatusText()
end

handlers.STATUS = function(...)
    WT.lastStatus = strjoin("|", ...)
    WT.Print("proxy: " .. WT.lastStatus)
    WT.UpdateStatusText()
end

handlers.CLEARCACHE = function()
    WT.Print("translation cache cleared.")
end

handlers.NAME = function(raw, english)
    if WT.OnNameTranslated then WT.OnNameTranslated(raw, english) end
end

local function OnAddonMessage(prefix, text, channel, sender)
    if prefix ~= WT.PREFIX or channel ~= "WHISPER" then return end
    if text:sub(1, 1) ~= "!" then return end -- our own request bouncing back from a proxy without translation
    local op, rest = text:match("^!(%u+)|?(.*)$")
    if not op then return end
    local handler = handlers[op]
    if handler then
        handler(strsplit("|", rest))
    end
end

-- ---------------------------------------------------------------------------
-- Settings changes (used by slash commands, the button and the config panel)
-- ---------------------------------------------------------------------------

function WT.Set(key, value)
    WoWTranslateDB[key] = value
    WT.PushConfig()
    WT.UpdateStatusText()
end

function WT.SetEnabled(v)
    WT.Set("enabled", v)
    WT.Print("translation " .. (v and "|cff55ff55ON|r" or "|cffff5555OFF|r"))
end

function WT.SetIncoming(v)
    WT.Set("incoming", v)
    WT.Print("incoming CN->EN " .. (v and "|cff55ff55ON|r" or "|cffff5555OFF|r"))
end

function WT.SetOutgoing(v)
    WT.Set("outgoing", v)
    WT.Print("outgoing EN->CN " .. (v and "|cff55ff55ON|r" or "|cffff5555OFF|r"))
end

-- ---------------------------------------------------------------------------
-- Slash commands (also for macros, e.g. a macro with just: /wt out toggle)
-- ---------------------------------------------------------------------------

local function ParseSwitch(arg, current)
    if arg == "on" or arg == "1" then return true end
    if arg == "off" or arg == "0" then return false end
    if arg == "toggle" or arg == "" or arg == nil then return not current end
    return nil
end

local function Help()
    WT.Print("commands:")
    WT.Print("  /wt - open settings")
    WT.Print("  /wt on | off | toggle - all translation")
    WT.Print("  /wt out on | off | toggle - outgoing EN->CN")
    WT.Print("  /wt in on | off | toggle - incoming CN->EN")
    WT.Print("  /wt names on | off | both | english - sender names")
    WT.Print("  /wt channels on | off - channel names (世界 -> World)")
    WT.Print("  /wt orig in | out on | off | toggle - also show the original text")
    WT.Print("  /wt button on | off - the OUT button on screen")
    WT.Print("  /wt font on | off - same font and size for Chinese and English in chat")
    WT.Print("  /wt status | clearcache | reconnect")
end

SLASH_WOWTRANSLATE1 = "/wt"
SLASH_WOWTRANSLATE2 = "/wowtranslate"
SlashCmdList.WOWTRANSLATE = function(input)
    local cmd, arg = strsplit(" ", strtrim(input or ""):lower(), 2)
    arg = arg and strtrim(arg) or ""
    local db = WoWTranslateDB

    if cmd == "" or cmd == "show" or cmd == "config" then
        WT.ToggleConfig()
    elseif cmd == "on" then
        WT.SetEnabled(true)
    elseif cmd == "off" then
        WT.SetEnabled(false)
    elseif cmd == "toggle" then
        WT.SetEnabled(not db.enabled)
    elseif cmd == "out" or cmd == "outgoing" then
        local v = ParseSwitch(arg, db.outgoing)
        if v == nil then return Help() end
        WT.SetOutgoing(v)
    elseif cmd == "in" or cmd == "incoming" then
        local v = ParseSwitch(arg, db.incoming)
        if v == nil then return Help() end
        WT.SetIncoming(v)
    elseif cmd == "names" then
        if arg == "both" or arg == "english" then
            WT.Set("nameStyle", arg)
            WT.Set("translateNames", true)
            WT.Print("sender names: " .. (arg == "both" and "Original | Translated" or "Translated"))
            return
        end
        local v = ParseSwitch(arg, db.translateNames)
        if v == nil then return Help() end
        WT.Set("translateNames", v)
        WT.Print("sender names " .. (v and "translated" or "as is"))
    elseif cmd == "channels" then
        local v = ParseSwitch(arg, db.translateChannels)
        if v == nil then return Help() end
        WT.Set("translateChannels", v)
        WT.Print("channel names " .. (v and "translated" or "as is"))
    elseif cmd == "orig" or cmd == "original" then
        local which, sw = strsplit(" ", arg, 2)
        local key = (which == "in" and "showOriginalIn") or (which == "out" and "showOriginalOut") or nil
        if not key then return Help() end
        local v = ParseSwitch(sw and strtrim(sw) or "", db[key])
        if v == nil then return Help() end
        WT.Set(key, v)
        WT.Print("original text on " .. (which == "in" and "incoming" or "your own") .. " messages " .. (v and "shown" or "hidden"))
    elseif cmd == "button" then
        local v = ParseSwitch(arg, db.showOutButton)
        if v == nil then return Help() end
        WT.Set("showOutButton", v)
    elseif cmd == "font" then
        local v = ParseSwitch(arg, db.unifyChatFont)
        if v == nil then return Help() end
        WT.SetUnifyChatFont(v)
        WT.Print("chat font " .. (v and "unified (WarSans)" or "back to the client's own"))
    elseif cmd == "status" then
        WT.Print(WT.StatusText())
        if WT.proxyVersion then WT.Send("?STATUS") end
    elseif cmd == "clearcache" then
        WT.Send("?CLEARCACHE")
    elseif cmd == "reconnect" or cmd == "hello" then
        WT.proxyVersion = nil
        WT.Hello()
    else
        Help()
    end
end

-- ---------------------------------------------------------------------------
-- Events
-- ---------------------------------------------------------------------------

local events = CreateFrame("Frame")
events:RegisterEvent("ADDON_LOADED")
events:RegisterEvent("PLAYER_ENTERING_WORLD")
events:RegisterEvent("CHAT_MSG_ADDON")
events:SetScript("OnEvent", function(_, event, ...)
    if event == "ADDON_LOADED" then
        if ... ~= ADDON then return end
        WoWTranslateDB = WoWTranslateDB or {}
        local db = WoWTranslateDB
        if db.showOriginal ~= nil then -- v2.0.0 had one switch for both
            db.showOriginalIn, db.showOriginalOut, db.showOriginal = db.showOriginal, db.showOriginal, nil
        end
        CopyDefaults(DEFAULTS, db)
        C_ChatInfo.RegisterAddonMessagePrefix(WT.PREFIX)
        if WT.OnLoaded then WT.OnLoaded() end
    elseif event == "PLAYER_ENTERING_WORLD" then
        -- Every login, reload and zone-in: the proxy may be a fresh session.
        WT.Hello()
    elseif event == "CHAT_MSG_ADDON" then
        OnAddonMessage(...)
    end
end)

-- Modules hook in here (OnLoaded runs once settings exist).
local loadedHooks = {}
function WT.AddLoadedHook(fn) table.insert(loadedHooks, fn) end
function WT.OnLoaded() for _, fn in ipairs(loadedHooks) do fn() end end

local stateHooks = {}
function WT.AddStateHook(fn) table.insert(stateHooks, fn) end
function WT.OnStateChanged() for _, fn in ipairs(stateHooks) do fn() end end
