-- Settings window (/wt, or click the minimap button).

local _, WT = ...

local frame
local refreshers = {}  -- functions that sync the widgets with WoWTranslateDB
local channelRows = {}

local function AddCheck(parent, label, x, y, get, set, tooltip)
    local cb = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
    cb:SetSize(24, 24)
    cb:SetPoint("TOPLEFT", x, y)
    local text = cb:CreateFontString(nil, "ARTWORK", "GameFontHighlight")
    text:SetPoint("LEFT", cb, "RIGHT", 2, 1)
    text:SetText(label or "")
    cb.label = text
    cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
    if tooltip then
        cb:SetScript("OnEnter", function(self)
            GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
            GameTooltip:AddLine(tooltip, 1, 1, 1, true)
            GameTooltip:Show()
        end)
        cb:SetScript("OnLeave", GameTooltip_Hide)
    end
    table.insert(refreshers, function() cb:SetChecked(get()) end)
    return cb
end

local function AddText(parent, text, x, y, font)
    local fs = parent:CreateFontString(nil, "ARTWORK", font or "GameFontNormal")
    fs:SetPoint("TOPLEFT", x, y)
    fs:SetText(text)
    return fs
end

-- Joined chat channels ("World", "LookingForGroup", ...). GetChannelList returns
-- id, name, disabled triples on this client.
local function JoinedChannels()
    local list = {}
    local values = { GetChannelList() }
    local step = (type(values[3]) == "boolean" or values[3] == nil) and 3 or 2
    for i = 1, #values, step do
        local name = values[i + 1]
        if type(name) == "string" and name ~= "" then
            table.insert(list, name)
        end
    end
    return list
end

local CHANNEL_TOP = -392

local function RefreshChannels()
    for _, row in ipairs(channelRows) do
        row.name:Hide(); row.inCheck:Hide(); row.outCheck:Hide()
    end
    local db = WoWTranslateDB
    local y = CHANNEL_TOP
    for i, name in ipairs(JoinedChannels()) do
        local row = channelRows[i]
        if not row then
            row = {}
            row.name = frame:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall")
            row.inCheck = CreateFrame("CheckButton", nil, frame, "UICheckButtonTemplate")
            row.outCheck = CreateFrame("CheckButton", nil, frame, "UICheckButtonTemplate")
            row.inCheck:SetSize(22, 22)
            row.outCheck:SetSize(22, 22)
            channelRows[i] = row
        end
        local key = name:lower()
        row.name:ClearAllPoints(); row.name:SetPoint("TOPLEFT", 40, y - 5); row.name:SetText("   " .. name)
        row.inCheck:ClearAllPoints(); row.inCheck:SetPoint("TOPLEFT", 230, y)
        row.outCheck:ClearAllPoints(); row.outCheck:SetPoint("TOPLEFT", 310, y)
        row.inCheck:SetChecked(not db.inChannelsOff[key])
        row.outCheck:SetChecked(not db.outChannelsOff[key])
        row.inCheck:SetScript("OnClick", function(self)
            db.inChannelsOff[key] = (not self:GetChecked()) or nil
            WT.PushConfig()
        end)
        row.outCheck:SetScript("OnClick", function(self)
            db.outChannelsOff[key] = (not self:GetChecked()) or nil
            WT.PushConfig()
        end)
        row.name:Show(); row.inCheck:Show(); row.outCheck:Show()
        y = y - 22
    end
    frame:SetHeight(-y + 60)
end

local function Refresh()
    if not frame or not frame:IsShown() then return end
    for _, fn in ipairs(refreshers) do fn() end
    frame.status:SetText(WT.StatusText())
    RefreshChannels()
end

local function Create()
    frame = CreateFrame("Frame", "WoWTranslateConfigFrame", UIParent, "BasicFrameTemplateWithInset")
    frame:SetSize(420, 520)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    frame:SetMovable(true)
    frame:EnableMouse(true)
    frame:SetClampedToScreen(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    tinsert(UISpecialFrames, frame:GetName()) -- Escape closes it

    frame.TitleText:SetText("WoWTranslate - Chinese <-> English")

    frame.status = frame:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall")
    frame.status:SetPoint("TOPLEFT", 16, -32)
    frame.status:SetPoint("TOPRIGHT", -16, -32)
    frame.status:SetJustifyH("LEFT")

    local db = function() return WoWTranslateDB end
    local y = -60
    AddCheck(frame, "Translation on (everything)", 16, y,
        function() return db().enabled end, function(v) WT.SetEnabled(v) end,
        "Master switch. Macro: /wt toggle")
    y = y - 26
    AddCheck(frame, "Incoming: Chinese -> English", 16, y,
        function() return db().incoming end, function(v) WT.SetIncoming(v) end,
        "Chat from others is shown in English. Macro: /wt in toggle")
    y = y - 26
    AddCheck(frame, "Outgoing: my English -> Chinese", 16, y,
        function() return db().outgoing end, function(v) WT.SetOutgoing(v) end,
        "What you type in English is sent in Chinese. Macro: /wt out toggle")
    y = y - 26
    AddCheck(frame, "Translate sender names in chat", 16, y,
        function() return db().translateNames end, function(v) WT.Set("translateNames", v) end,
        "Shows Chinese player names in English. Clicking the name still uses the real name.")
    y = y - 26
    AddCheck(frame, "Also show the original text", 16, y,
        function() return db().showOriginal end, function(v) WT.Set("showOriginal", v) end,
        "Adds the original Chinese in brackets after the English (and your English after what you sent).")
    y = y - 26
    AddCheck(frame, "Show the OUT button", 16, y,
        function() return db().showOutButton end, function(v) WT.Set("showOutButton", v) end,
        "On-screen button to switch outgoing translation. Shift-drag to move it.")
    y = y - 26
    AddCheck(frame, "Show the minimap button", 16, y,
        function() return not db().minimap.hide end, function(v) WT.SetMinimapShown(v) end)

    y = y - 36
    AddText(frame, "Chat", 40, y)
    AddText(frame, "Incoming", 214, y)
    AddText(frame, "Outgoing", 294, y)
    y = y - 18
    for _, t in ipairs(WT.CHAT_TYPES) do
        local code = t.code
        AddText(frame, t.label, 40, y - 5, "GameFontHighlightSmall")
        AddCheck(frame, nil, 230, y,
            function() return db().inTypes[code] end,
            function(v) db().inTypes[code] = v; WT.PushConfig() end)
        AddCheck(frame, nil, 310, y,
            function() return db().outTypes[code] end,
            function(v) db().outTypes[code] = v; WT.PushConfig() end)
        y = y - 22
    end
    CHANNEL_TOP = y - 4

    local check = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    check:SetSize(110, 22)
    check:SetPoint("BOTTOMLEFT", 16, 12)
    check:SetText("Check proxy")
    check:SetScript("OnClick", function()
        WT.proxyVersion = nil
        WT.Hello()
        if WT.proxyVersion == nil then frame.status:SetText("Checking...") end
    end)

    local clear = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    clear:SetSize(110, 22)
    clear:SetPoint("LEFT", check, "RIGHT", 8, 0)
    clear:SetText("Clear cache")
    clear:SetScript("OnClick", function()
        WT.Send("?CLEARCACHE")
        wipe(WoWTranslateDB.names)
    end)

    local status = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    status:SetSize(110, 22)
    status:SetPoint("LEFT", clear, "RIGHT", 8, 0)
    status:SetText("Proxy status")
    status:SetScript("OnClick", function() WT.Send("?STATUS") end)

    frame:SetScript("OnShow", Refresh)
    frame:RegisterEvent("CHANNEL_UI_UPDATE")
    frame:SetScript("OnEvent", Refresh)
end

function WT.ToggleConfig()
    if not frame then Create() end
    if frame:IsShown() then frame:Hide() else frame:Show() end
end

WT.AddStateHook(Refresh)
