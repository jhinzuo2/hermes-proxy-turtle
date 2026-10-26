-- Settings window (/wt, or click the minimap button).
-- Left: what the options do, each with a short description. Right: which chat gets translated.

local _, WT = ...

local frame
local refreshers = {}  -- functions that sync the widgets with WoWTranslateDB
local channelRows = {}

local LEFT_X, LEFT_W = 16, 360
local RIGHT_X = 400
local COL_IN, COL_OUT = RIGHT_X + 190, RIGHT_X + 260
local channelTop = 0

local function DB() return WoWTranslateDB end

local function AddText(parent, text, x, y, font, width)
    local fs = parent:CreateFontString(nil, "ARTWORK", font or "GameFontNormal")
    fs:SetPoint("TOPLEFT", x, y)
    fs:SetJustifyH("LEFT")
    if width then fs:SetWidth(width) end
    fs:SetText(text)
    return fs
end

local function TextHeight(fs, fallback)
    local h = fs.GetStringHeight and fs:GetStringHeight()
    return (type(h) == "number" and h > 0) and h or fallback
end

local function AddSection(parent, title, x, y)
    AddText(parent, title, x, y, "GameFontNormalLarge")
    return y - 22
end

-- Checkbox with a label and, under it, a grey description. Returns the y below it.
local function AddOption(parent, x, y, label, description, get, set)
    local cb = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
    cb:SetSize(24, 24)
    cb:SetPoint("TOPLEFT", x, y)
    cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
    table.insert(refreshers, function() cb:SetChecked(get()) end)

    AddText(parent, label, x + 26, y - 5, "GameFontHighlight", LEFT_W - (x - LEFT_X) - 26)
    y = y - 24
    if description then
        local desc = AddText(parent, description, x + 26, y, "GameFontDisableSmall", LEFT_W - (x - LEFT_X) - 26)
        desc:SetTextColor(0.7, 0.7, 0.7)
        y = y - TextHeight(desc, 24) - 6
    end
    return y
end

local function AddTableCheck(parent, x, y, get, set)
    local cb = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
    cb:SetSize(22, 22)
    cb:SetPoint("TOPLEFT", x, y)
    cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
    table.insert(refreshers, function() cb:SetChecked(get()) end)
    return cb
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

local function RefreshChannels()
    for _, row in ipairs(channelRows) do
        row.name:Hide(); row.inCheck:Hide(); row.outCheck:Hide()
    end
    local db = DB()
    local y = channelTop
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
        row.name:ClearAllPoints(); row.name:SetPoint("TOPLEFT", RIGHT_X + 12, y - 5); row.name:SetText(name)
        row.inCheck:ClearAllPoints(); row.inCheck:SetPoint("TOPLEFT", COL_IN, y)
        row.outCheck:ClearAllPoints(); row.outCheck:SetPoint("TOPLEFT", COL_OUT, y)
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
    return y
end

local leftBottom = 0

local function Refresh()
    if not frame or not frame:IsShown() then return end
    for _, fn in ipairs(refreshers) do fn() end
    frame.status:SetText(WT.StatusText())
    local rightBottom = RefreshChannels()
    frame:SetHeight(-math.min(leftBottom, rightBottom) + 50)
end

local function BuildLeft()
    local db = DB
    local x, y = LEFT_X, -58

    y = AddSection(frame, "Translation", x, y)
    y = AddOption(frame, x, y, "Translation on",
        "Master switch for everything below. Off = chat passes through untouched. Macro: /wt toggle",
        function() return db().enabled end, function(v) WT.SetEnabled(v) end)
    y = AddOption(frame, x, y, "Incoming: Chinese -> English",
        "Chinese chat from others is shown in English. A translated line arrives up to a second later. " ..
        "Macro: /wt in toggle",
        function() return db().incoming end, function(v) WT.SetIncoming(v) end)
    y = AddOption(frame, x, y, "Outgoing: my English -> Chinese",
        "What you type in English is sent in Chinese. Things you type in Chinese and .commands go out as typed. " ..
        "Same as the OUT button. Macro: /wt out toggle",
        function() return db().outgoing end, function(v) WT.SetOutgoing(v) end)

    y = AddSection(frame, "Names", x, y - 6)
    y = AddOption(frame, x, y, "Translate player names in chat",
        "Chinese sender names are shown in English. Display only: clicking a name still whispers, invites or " ..
        "targets the real player. A new name shows untranslated on its first message.",
        function() return db().translateNames end, function(v) WT.Set("translateNames", v) end)
    y = AddOption(frame, x + 20, y, "Keep the original name",
        "[风之子 | Son Of The Wind] instead of [Son Of The Wind]. /wt names both | english",
        function() return db().nameStyle == "both" end,
        function(v) WT.Set("nameStyle", v and "both" or "english") end)
    y = AddOption(frame, x + 20, y, "Also in system lines",
        "\"[丶喀秋莎] has come online\" and other lines the game writes with a player name in them. /wt sysnames",
        function() return db().translateSystemNames end, function(v) WT.Set("translateSystemNames", v) end)
    y = AddOption(frame, x, y, "Translate channel names",
        "[5. 世界] is shown as [5. World]. Display only: /5 still talks in the same channel. /wt channels",
        function() return db().translateChannels end, function(v) WT.Set("translateChannels", v) end)

    y = AddSection(frame, "Show the original", x, y - 6)
    y = AddOption(frame, x, y, "On incoming messages",
        "English (中文): handy to check a translation. /wt orig in toggle",
        function() return db().showOriginalIn end, function(v) WT.Set("showOriginalIn", v) end)
    y = AddOption(frame, x, y, "On my own messages",
        "You always see what you typed; this adds the Chinese that was actually sent. /wt orig out toggle",
        function() return db().showOriginalOut end, function(v) WT.Set("showOriginalOut", v) end)

    y = AddSection(frame, "Display", x, y - 6)
    y = AddOption(frame, x, y, "Same font and size for Chinese and English",
        "The game draws any chat line with Chinese in it in a bigger font. This uses one font (Nowar WarSans) " ..
        "for both, at your chat font size. /wt font",
        function() return db().unifyChatFont end, function(v) WT.SetUnifyChatFont(v) end)
    y = AddOption(frame, x, y, "Show the OUT button",
        "Click it to switch outgoing translation. Shift-drag to move it. /wt button",
        function() return db().showOutButton end, function(v) WT.Set("showOutButton", v) end)
    y = AddOption(frame, x, y, "Show the minimap button",
        "Opens this window. Drag it around the minimap.",
        function() return not db().minimap.hide end, function(v) WT.SetMinimapShown(v) end)
    return y
end

local function BuildRight()
    local db = DB
    local y = -58
    y = AddSection(frame, "What gets translated", RIGHT_X, y)
    local note = AddText(frame, "Incoming = what others write to you. Outgoing = what you write.",
        RIGHT_X, y, "GameFontDisableSmall", 380)
    note:SetTextColor(0.7, 0.7, 0.7)
    y = y - TextHeight(note, 14) - 8

    AddText(frame, "Chat", RIGHT_X + 12, y)
    AddText(frame, "Incoming", COL_IN - 16, y)
    AddText(frame, "Outgoing", COL_OUT - 14, y)
    y = y - 20
    for _, t in ipairs(WT.CHAT_TYPES) do
        local code = t.code
        AddText(frame, t.label, RIGHT_X + 12, y - 5, "GameFontHighlightSmall")
        AddTableCheck(frame, COL_IN, y,
            function() return db().inTypes[code] end,
            function(v) db().inTypes[code] = v; WT.PushConfig() end)
        if not t.incomingOnly then
            AddTableCheck(frame, COL_OUT, y,
                function() return db().outTypes[code] end,
                function(v) db().outTypes[code] = v; WT.PushConfig() end)
        end
        y = y - 22
    end

    y = y - 10
    AddText(frame, "Channels you are in", RIGHT_X + 12, y)
    y = y - 16
    local hint = AddText(frame, "Only used while \"Channels\" above is ticked.", RIGHT_X + 12, y,
        "GameFontDisableSmall", 360)
    hint:SetTextColor(0.7, 0.7, 0.7)
    channelTop = y - TextHeight(hint, 12) - 4
end

local function Create()
    frame = CreateFrame("Frame", "WoWTranslateConfigFrame", UIParent, "BasicFrameTemplateWithInset")
    frame:SetSize(LEFT_W + 420, 600)
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

    leftBottom = BuildLeft()
    BuildRight()

    local check = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    check:SetSize(120, 22)
    check:SetPoint("BOTTOMLEFT", 16, 12)
    check:SetText("Check proxy")
    check:SetScript("OnClick", function()
        WT.proxyVersion = nil
        frame.status:SetText("Checking...")
        WT.Hello()
    end)

    local status = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    status:SetSize(120, 22)
    status:SetPoint("LEFT", check, "RIGHT", 8, 0)
    status:SetText("Proxy status")
    status:SetScript("OnClick", function() WT.Send("?STATUS") end)

    local clear = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    clear:SetSize(120, 22)
    clear:SetPoint("LEFT", status, "RIGHT", 8, 0)
    clear:SetText("Clear cache")
    clear:SetScript("OnClick", function()
        WT.Send("?CLEARCACHE")
        wipe(WoWTranslateDB.names)
    end)
    clear:SetScript("OnEnter", function(self)
        GameTooltip:SetOwner(self, "ANCHOR_TOP")
        GameTooltip:AddLine("Forget all saved translations and names, so they are translated again.", 1, 1, 1, true)
        GameTooltip:Show()
    end)
    clear:SetScript("OnLeave", GameTooltip_Hide)

    frame:SetScript("OnShow", Refresh)
    frame:RegisterEvent("CHANNEL_UI_UPDATE")
    frame:SetScript("OnEvent", Refresh)
end

function WT.ToggleConfig()
    if not frame then Create() end
    if frame:IsShown() then frame:Hide() else frame:Show() end
end

WT.AddStateHook(Refresh)
