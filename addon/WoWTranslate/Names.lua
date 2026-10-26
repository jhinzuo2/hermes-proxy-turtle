-- Names in chat, display only:
--  * sender names: "风之子" shows as "Son Of The Wind", or "风之子 | Son Of The Wind". The player
--    link underneath keeps the real name, so whisper, invite and target still work.
--  * channel names: "[5. 世界]" shows as "[5. World]". Typing into the channel is unchanged.
--
-- This client draws a whole chat line in its (much bigger) CJK font as soon as the line has one
-- Chinese character in it, so English names and channels also keep line sizes even.

local _, WT = ...

local pending = {}  -- name -> true while the proxy is translating it

-- Common Chinese-client channel names; anything else is asked from the proxy like a name.
local CHANNELS = {
    ["世界"] = "World",
    ["综合"] = "General",
    ["交易"] = "Trade",
    ["本地防务"] = "LocalDefense",
    ["世界防务"] = "WorldDefense",
    ["寻求组队"] = "LookingForGroup",
    ["公会招募"] = "GuildRecruitment",
    ["硬核"] = "Hardcore",
}

local function HasChinese(s)
    -- UTF-8 lead bytes 0xE3-0xE9 cover CJK punctuation and the CJK ideograph blocks.
    return s and s:find("[\227-\233][\128-\191][\128-\191]") ~= nil
end

local function EscapePattern(s)
    return (s:gsub("([%(%)%.%%%+%-%*%?%[%]%^%$])", "%%%1"))
end

local nameListeners = {}
-- fn(raw, english) runs when the proxy answers a name (e.g. to refresh an open tooltip).
function WT.AddNameListener(fn) table.insert(nameListeners, fn) end

function WT.OnNameTranslated(raw, english)
    if raw and english and english ~= "" then
        WoWTranslateDB.names[raw] = english
    end
    pending[raw] = nil
    for _, fn in ipairs(nameListeners) do fn(raw, english) end
end

-- English for a Chinese name, or nil (then asks the proxy, so a later line has it).
local function Lookup(raw)
    local english = WoWTranslateDB.names[raw]
    if english then return english end
    if WT.proxyVersion and not pending[raw] then
        pending[raw] = true
        WT.Send("?NAME|" .. raw)
        -- Forget a request the proxy never answered so it can be asked again later.
        C_Timer.After(30, function() pending[raw] = nil end)
    end
    return nil
end

WT.HasChinese = HasChinese
WT.LookupName = Lookup -- English for a Chinese name, or nil (and asks the proxy)

local function SenderDisplay(raw)
    local english = Lookup(raw)
    if not english then return nil end
    if WoWTranslateDB.nameStyle == "both" then
        return raw .. " | " .. english
    end
    return english
end
WT.NameDisplay = SenderDisplay -- "English" or "原名 | English" per the name style; nil = not known yet

-- Replaces each run of non-ASCII text ("世界", "暴风城") with its English name.
local function TranslateChannelText(text)
    if type(text) ~= "string" or not HasChinese(text) then return text end
    return (text:gsub("[\128-\255]+", function(run)
        if not HasChinese(run) then return run end
        return CHANNELS[run] or Lookup(run) or run
    end))
end

local function ChannelFilter(_, _, ...)
    local db = WoWTranslateDB
    if not (db.enabled and db.translateChannels) then return false end
    local args = { ... }
    local channelString, channelName = args[4], args[9]
    if not (HasChinese(channelString) or HasChinese(channelName)) then return false end
    args[4] = TranslateChannelText(channelString)
    args[9] = TranslateChannelText(channelName)
    return false, unpack(args, 1, select("#", ...))
end

-- System lines the client builds itself ("[丶喀秋莎] has come online", friend/who notices):
-- swap the [display] part of player links. The link keeps the real name.
local function SystemFilter(_, _, msg, ...)
    local db = WoWTranslateDB
    if not (db.enabled and db.translateNames and db.translateSystemNames) then return false end
    if type(msg) ~= "string" or not HasChinese(msg) then return false end
    local changed = false
    local newMsg = msg:gsub("(|Hplayer:[^|]*|h)%[([^%]]+)%](|h)", function(open, shown, close)
        local raw = Ambiguate(shown, "none")
        if not HasChinese(raw) then return nil end
        local display = SenderDisplay(raw)
        if not display then return nil end
        changed = true
        return open .. "[" .. display .. "]" .. close
    end)
    -- Lines without a link ("丶喀秋莎 has gone offline."): a Chinese run in a short system
    -- line is taken as a name. Longer lines are server text, which the proxy translates.
    if not changed and not msg:find("|H", 1, true) and #msg <= 120 then
        newMsg = msg:gsub("[\128-\255]+", function(run)
            if not HasChinese(run) then return nil end
            local display = SenderDisplay(run)
            if display then changed = true end
            return display
        end)
    end
    if not changed then return false end
    return false, newMsg, ...
end

WT.AddLoadedHook(function()
    ChatFrame_AddMessageEventFilter("CHAT_MSG_SYSTEM", SystemFilter)

    local original = GetColoredName
    GetColoredName = function(event, arg1, arg2, ...)
        local colored = original(event, arg1, arg2, ...)
        local db = WoWTranslateDB
        if not (db.enabled and db.translateNames) or type(arg2) ~= "string" then
            return colored
        end
        local raw = Ambiguate(arg2, "none")
        if not HasChinese(raw) then
            return colored
        end
        local shown = SenderDisplay(raw)
        if not shown then
            return colored
        end
        local replaced, count = colored:gsub(EscapePattern(raw), (shown:gsub("%%", "%%%%")), 1)
        return count > 0 and replaced or colored
    end

    for _, event in ipairs({ "CHAT_MSG_CHANNEL", "CHAT_MSG_CHANNEL_JOIN", "CHAT_MSG_CHANNEL_LEAVE",
                             "CHAT_MSG_CHANNEL_NOTICE", "CHAT_MSG_CHANNEL_NOTICE_USER" }) do
        ChatFrame_AddMessageEventFilter(event, ChannelFilter)
    end
end)
