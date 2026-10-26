-- Player tooltips: Chinese name, guild name and guild rank shown in English (display only).
-- Uses the same name translations as chat; a name not known yet is asked from the proxy and
-- the tooltip updates when the answer comes while you still hover that player.

local _, WT = ...

local WARSANS = "Interface\\AddOns\\WoWTranslate\\Fonts\\WarSansTT-Bliz-500.ttf"

local changedFonts = {}  -- tooltip line -> its font object before we touched it
local current = {}       -- raw strings of the player under the tooltip, for refreshing

local function Enabled()
    local db = WoWTranslateDB
    return db.enabled and db.translateNames and db.translateTooltips
end

local function Plain(text)
    return text:gsub("([%(%)%.%%%+%-%*%?%[%]%^%$])", "%%%1")
end

-- Lines that still show Chinese ("原名 | English") get WarSans so they aren't drawn bigger.
local function UseWarSans(line)
    if changedFonts[line] then return end
    local _, size, flags = line:GetFont()
    changedFonts[line] = line:GetFontObject()
    line:SetFont(WARSANS, size or 12, flags or "")
end

local function RestoreFonts()
    for line, fontObject in pairs(changedFonts) do
        if fontObject then line:SetFontObject(fontObject) end
    end
    wipe(changedFonts)
end

-- Replaces raw (once) in any left-hand line with its display form.
local function ReplaceInLines(tooltip, raw, fromLine)
    local shown = WT.NameDisplay(raw)
    if not shown then return false end
    local name = tooltip:GetName()
    for i = fromLine, tooltip:NumLines() do
        local line = _G[name .. "TextLeft" .. i]
        local text = line and line:GetText()
        if text and text:find(raw, 1, true) then
            line:SetText((text:gsub(Plain(raw), (shown:gsub("%%", "%%%%")), 1)))
            if WT.HasChinese(shown) then UseWarSans(line) end
            return true
        end
    end
    return false
end

local function Apply(tooltip)
    if not Enabled() then return end
    local _, unit = tooltip:GetUnit()
    if not unit or not UnitIsPlayer(unit) then return end

    local name = UnitName(unit)
    local guild, rank = GetGuildInfo(unit)
    current.name = name and WT.HasChinese(name) and name or nil
    current.guild = guild and WT.HasChinese(guild) and guild or nil
    current.rank = rank and WT.HasChinese(rank) and rank or nil
    if not (current.name or current.guild or current.rank) then return end

    local changed = false
    if current.name and ReplaceInLines(tooltip, current.name, 1) then changed = true end
    if current.guild and ReplaceInLines(tooltip, current.guild, 2) then changed = true end
    if current.rank and ReplaceInLines(tooltip, current.rank, 2) then changed = true end
    if changed then tooltip:Show() end -- resize to the new text
end

WT.AddLoadedHook(function()
    GameTooltip:HookScript("OnTooltipSetUnit", Apply)
    GameTooltip:HookScript("OnTooltipCleared", function()
        RestoreFonts()
        wipe(current)
    end)

    -- Answer for the player still under the mouse: rebuild the tooltip so it shows up now.
    WT.AddNameListener(function(raw)
        if not GameTooltip:IsShown() then return end
        if raw ~= current.name and raw ~= current.guild and raw ~= current.rank then return end
        local _, unit = GameTooltip:GetUnit()
        if unit then GameTooltip:SetUnit(unit) end
    end)
end)
