-- Friends list and guild window: Chinese player names (and guild ranks) shown in English.
-- Display only: whisper, invite and the right-click menus keep using the real names. Lists are
-- narrow, so they always show just the English, whatever the chat name style is.

local _, WT = ...

local function Enabled()
    local db = WoWTranslateDB
    return db.enabled and db.translateNames and db.translateSocial
end

-- Swaps every Chinese run in a font string for its English name, when that is known.
local function TranslateFontString(fs)
    if not fs or not fs.GetText then return end
    local text = fs:GetText()
    if type(text) ~= "string" or not WT.HasChinese(text) then return end
    local changed = false
    local newText = text:gsub("[\128-\255]+", function(run)
        if not WT.HasChinese(run) then return nil end
        local english = WT.LookupName(run)
        if english then changed = true end
        return english
    end)
    if changed then fs:SetText(newText) end
end

local function UpdateFriendButton(button)
    if not Enabled() or not button then return end
    TranslateFontString(button.name)
end

local function UpdateFriendsList()
    if not Enabled() then return end
    local scroll = FriendsListFrameScrollFrame
    if scroll and scroll.buttons then
        for _, button in ipairs(scroll.buttons) do
            if button:IsShown() then TranslateFontString(button.name) end
        end
    end
end

local function UpdateGuildList()
    if not Enabled() then return end
    for i = 1, (GUILDMEMBERS_TO_DISPLAY or 13) do
        TranslateFontString(_G["GuildFrameButton" .. i .. "Name"])
        TranslateFontString(_G["GuildFrameGuildStatusButton" .. i .. "Name"])
        TranslateFontString(_G["GuildFrameGuildStatusButton" .. i .. "Rank"])
    end
    TranslateFontString(_G.GuildMemberDetailName)
    TranslateFontString(_G.GuildMemberDetailRank)
end

-- Several names usually arrive at once: redraw the open list once, a moment later.
local refreshQueued = false
local function QueueRefresh()
    if refreshQueued then return end
    refreshQueued = true
    C_Timer.After(0.3, function()
        refreshQueued = false
        if not Enabled() or not (FriendsFrame and FriendsFrame:IsShown()) then return end
        if FriendsList_Update and FriendsListFrame and FriendsListFrame:IsShown() then FriendsList_Update() end
        if GuildStatus_Update and GuildFrame and GuildFrame:IsShown() then GuildStatus_Update() end
    end)
end

WT.AddLoadedHook(function()
    if FriendsFrame_UpdateFriendButton then
        hooksecurefunc("FriendsFrame_UpdateFriendButton", UpdateFriendButton)
    elseif FriendsList_Update then
        hooksecurefunc("FriendsList_Update", UpdateFriendsList)
    end
    if GuildStatus_Update then
        hooksecurefunc("GuildStatus_Update", UpdateGuildList)
    end
    if GuildMemberDetailFrame then
        GuildMemberDetailFrame:HookScript("OnShow", UpdateGuildList)
    end
    WT.AddNameListener(QueueRefresh)
end)

-- Switching the option on or off redraws the open lists (off = the real names come back).
function WT.SetTranslateSocial(on)
    WT.Set("translateSocial", on)
    if FriendsList_Update and FriendsListFrame and FriendsListFrame:IsShown() then FriendsList_Update() end
    if GuildStatus_Update and GuildFrame and GuildFrame:IsShown() then GuildStatus_Update() end
end
