-- Social window (friends, who, ignore, guild): Chinese player names and guild ranks shown in
-- English. Display only: whisper, invite and the right-click menus keep using the real names.
-- Lists are narrow, so they always show just the English, whatever the chat name style is.
--
-- The 1.14 lists redraw their rows in several ways (hooks, scroll frames calling stored
-- functions), so instead of chasing each one: while the window is open, its visible text is
-- checked a few times a second. Only rows that still contain Chinese cost anything.

local _, WT = ...

local SCAN_INTERVAL = 0.5
local MAX_DEPTH = 8

local function Enabled()
    local db = WoWTranslateDB
    return db.enabled and db.translateNames and db.translateSocial
end

-- Swaps every Chinese run in a font string for its English name, when that is known.
local translating = false -- our own SetText must not re-enter the SetText hook
local function TranslateFontString(fs)
    if translating or not Enabled() then return end
    local text = fs:GetText()
    if type(text) ~= "string" or not WT.HasChinese(text) then return end
    local changed = false
    local newText = text:gsub("[\128-\255]+", function(run)
        if not WT.HasChinese(run) then return nil end
        local english = WT.LookupName(run)
        if english then changed = true end
        return english
    end)
    if changed then
        translating = true
        fs:SetText(newText)
        translating = false
    end
end

-- Each list line gets translated the moment the game writes into it (rows are reused while
-- scrolling), so the Chinese never shows. Installed once per line.
local hooked = setmetatable({}, { __mode = "k" })
local function HookFontString(fs)
    if hooked[fs] then return end
    hooked[fs] = true
    hooksecurefunc(fs, "SetText", TranslateFontString)
    if fs.SetFormattedText then hooksecurefunc(fs, "SetFormattedText", TranslateFontString) end
end

local function Scan(frame, depth)
    if depth > MAX_DEPTH or not frame:IsVisible() then return end
    for _, region in ipairs({ frame:GetRegions() }) do
        if region:GetObjectType() == "FontString" then
            HookFontString(region)
            if region:IsVisible() then TranslateFontString(region) end
        end
    end
    for _, child in ipairs({ frame:GetChildren() }) do
        Scan(child, depth + 1)
    end
end

-- Right-click menus on a player (lists and chat): the title line is the name. Display only;
-- the menu's actions use the name stored on the menu, not this text.
local function TranslateMenuTitle()
    if not Enabled() or not (DropDownList1 and DropDownList1:IsShown()) then return end
    local button = _G.DropDownList1Button1
    local fs = button and (button.GetFontString and button:GetFontString() or _G.DropDownList1Button1NormalText)
    if not fs then return end
    TranslateFontString(fs)
    -- Keep the menu wide enough for a longer English name.
    local needed = (fs:GetStringWidth() or 0) + 40
    if needed > DropDownList1:GetWidth() then
        DropDownList1:SetWidth(needed)
        for i = 1, (DropDownList1.numButtons or 0) do
            local b = _G["DropDownList1Button" .. i]
            if b then b:SetWidth(needed - 30) end
        end
    end
end

function WT.ScanSocial()
    if Enabled() and FriendsFrame and FriendsFrame:IsVisible() then
        Scan(FriendsFrame, 1)
    end
end

local ticker

WT.AddLoadedHook(function()
    if not FriendsFrame then return end
    FriendsFrame:HookScript("OnShow", function()
        WT.ScanSocial()
        if not ticker then ticker = C_Timer.NewTicker(SCAN_INTERVAL, WT.ScanSocial) end
    end)
    FriendsFrame:HookScript("OnHide", function()
        if ticker then ticker:Cancel(); ticker = nil end
    end)
    -- Redraw right away after the lists update, instead of waiting for the next tick.
    for _, name in ipairs({ "FriendsList_Update", "WhoList_Update", "IgnoreList_Update", "GuildStatus_Update" }) do
        if type(_G[name]) == "function" then hooksecurefunc(name, WT.ScanSocial) end
    end
    if GuildMemberDetailFrame then
        GuildMemberDetailFrame:HookScript("OnShow", function() Scan(GuildMemberDetailFrame, 1) end)
    end
    if ToggleDropDownMenu then
        hooksecurefunc("ToggleDropDownMenu", TranslateMenuTitle)
    end
end)

-- Switching the option off redraws the open lists so the real names come back.
function WT.SetTranslateSocial(on)
    WT.Set("translateSocial", on)
    if on then
        WT.ScanSocial()
        return
    end
    if FriendsList_Update and FriendsListFrame and FriendsListFrame:IsShown() then FriendsList_Update() end
    if WhoList_Update and WhoFrame and WhoFrame:IsShown() then WhoList_Update() end
    if GuildStatus_Update and GuildFrame and GuildFrame:IsShown() then GuildStatus_Update() end
end
