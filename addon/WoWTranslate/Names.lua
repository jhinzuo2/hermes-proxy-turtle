-- Sender names in chat: "风之子" is shown as "Son Of The Wind". Display only: the player
-- link underneath keeps the real name, so whisper, invite and target still work.

local _, WT = ...

local pending = {}  -- name -> true while the proxy is translating it

local function HasChinese(s)
    -- UTF-8 lead bytes 0xE3-0xE9 cover CJK punctuation and the CJK ideograph blocks.
    return s and s:find("[\227-\233][\128-\191][\128-\191]") ~= nil
end

local function EscapePattern(s)
    return (s:gsub("([%(%)%.%%%+%-%*%?%[%]%^%$])", "%%%1"))
end

function WT.OnNameTranslated(raw, english)
    if raw and english and english ~= "" then
        WoWTranslateDB.names[raw] = english
    end
    pending[raw] = nil
end

local function DisplayName(raw)
    local english = WoWTranslateDB.names[raw]
    if english then
        if WoWTranslateDB.showOriginal then
            return english .. " (" .. raw .. ")"
        end
        return english
    end
    if WT.proxyVersion and not pending[raw] then
        pending[raw] = true
        WT.Send("?NAME|" .. raw)
        -- Forget a request the proxy never answered so it can be asked again later.
        C_Timer.After(30, function() pending[raw] = nil end)
    end
    return nil
end

WT.AddLoadedHook(function()
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
        local shown = DisplayName(raw)
        if not shown then
            return colored
        end
        local replaced, count = colored:gsub(EscapePattern(raw), (shown:gsub("%%", "%%%%")), 1)
        return count > 0 and replaced or colored
    end
end)
