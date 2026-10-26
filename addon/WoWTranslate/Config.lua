-- Settings window (/wt, or click the minimap button). Fixed size, scrolls.
-- Left: what the options do, each with a short description. Right: which chat gets translated.

local _, WT = ...

local frame, content
local refreshers = {}  -- functions that sync the widgets with WoWTranslateDB
local channelRows = {}

local WIDTH, HEIGHT = 660, 480
local LEFT_X, LEFT_W = 4, 330
local RIGHT_X = 360
local COL_IN, COL_OUT = RIGHT_X + 160, RIGHT_X + 220
local channelTop = 0
local leftBottom = 0

-- Descriptions and channel names can contain Chinese: plain (non-family) fonts on WarSans keep
-- those lines the same size as the rest (see ChatFont.lua for why).
local WARSANS = "Interface\\AddOns\\WoWTranslate\\Fonts\\WarSansTT-Bliz-500.ttf"
local descFont = CreateFont("WoWTranslateDescFont")
descFont:SetFont(WARSANS, 10, "")
descFont:SetTextColor(0.7, 0.7, 0.7)
local rowFont = CreateFont("WoWTranslateRowFont")
rowFont:SetFont(WARSANS, 11, "")
rowFont:SetTextColor(1, 1, 1)

local function DB() return WoWTranslateDB end

local function AddText(parent, text, x, y, font, width)
    local fs = parent:CreateFontString(nil, "ARTWORK")
    fs:SetFontObject(font or "GameFontNormal")
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

local function AddSection(title, x, y)
    AddText(content, title, x, y, "GameFontNormal")
    return y - 18
end

-- Checkbox with a label and, under it, a grey description. Returns the y below it.
local function AddOption(x, y, label, description, get, set)
    local cb = CreateFrame("CheckButton", nil, content, "UICheckButtonTemplate")
    cb:SetSize(22, 22)
    cb:SetPoint("TOPLEFT", x, y)
    cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
    table.insert(refreshers, function() cb:SetChecked(get()) end)

    local width = LEFT_W - (x - LEFT_X) - 24
    AddText(content, label, x + 24, y - 4, "GameFontHighlightSmall", width)
    y = y - 20
    if description then
        local desc = AddText(content, description, x + 24, y, descFont, width)
        y = y - TextHeight(desc, 22) - 4
    end
    return y
end

local function AddTableCheck(x, y, get, set)
    local cb = CreateFrame("CheckButton", nil, content, "UICheckButtonTemplate")
    cb:SetSize(20, 20)
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
            row.name = content:CreateFontString(nil, "ARTWORK")
            row.name:SetFontObject(rowFont)
            row.inCheck = CreateFrame("CheckButton", nil, content, "UICheckButtonTemplate")
            row.outCheck = CreateFrame("CheckButton", nil, content, "UICheckButtonTemplate")
            row.inCheck:SetSize(20, 20)
            row.outCheck:SetSize(20, 20)
            channelRows[i] = row
        end
        local key = name:lower()
        row.name:ClearAllPoints(); row.name:SetPoint("TOPLEFT", RIGHT_X + 8, y - 4); row.name:SetText(name)
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
        y = y - 20
    end
    return y
end

local function Refresh()
    if not frame or not frame:IsShown() then return end
    for _, fn in ipairs(refreshers) do fn() end
    frame.status:SetText(WT.StatusText())
    local rightBottom = RefreshChannels()
    content:SetHeight(-math.min(leftBottom, rightBottom) + 10)
end

local function BuildLeft()
    local db = DB
    local x, y = LEFT_X, -4

    y = AddSection("Translation", x, y)
    y = AddOption(x, y, "Translation on",
        "Master switch for everything below. Off = chat passes through untouched. Macro: /wt toggle",
        function() return db().enabled end, function(v) WT.SetEnabled(v) end)
    y = AddOption(x, y, "Incoming: Chinese -> English",
        "Chinese chat from others is shown in English. A translated line arrives up to a second later. " ..
        "Macro: /wt in toggle",
        function() return db().incoming end, function(v) WT.SetIncoming(v) end)
    y = AddOption(x, y, "Outgoing: my English -> Chinese",
        "What you type in English is sent in Chinese. Things you type in Chinese and .commands go out as typed. " ..
        "Same as the OUT button. Macro: /wt out toggle",
        function() return db().outgoing end, function(v) WT.SetOutgoing(v) end)

    y = AddOption(x, y, "Pause while AFK",
        "No incoming translation while you are flagged AFK, so a busy channel doesn't keep the translation " ..
        "service busy. Starts again when you're back. /wt afk on | off",
        function() return db().pauseWhileAfk end, function(v) WT.Set("pauseWhileAfk", v) end)

    -- Translation service, switched live (no proxy restart).
    AddText(content, "Translation service", x + 24, y - 4, "GameFontHighlightSmall")
    local dd = CreateFrame("Frame", "WoWTranslateBackendDropDown", content, "UIDropDownMenuTemplate")
    dd:SetPoint("TOPLEFT", x + 130, y + 2)
    UIDropDownMenu_SetWidth(dd, 130)
    UIDropDownMenu_Initialize(dd, function()
        for _, name in ipairs(WT.BACKENDS) do
            local info = UIDropDownMenu_CreateInfo()
            info.text = name
            info.checked = (WT.proxyBackend == name)
            info.func = function()
                WT.SetBackend(name)
                UIDropDownMenu_SetText(dd, name)
            end
            UIDropDownMenu_AddButton(info)
        end
    end)
    table.insert(refreshers, function() UIDropDownMenu_SetText(dd, WT.proxyBackend or "?") end)
    y = y - 28
    local note = AddText(content, "auto = try them in order until one works. Pick one when the current one " ..
        "is slow or rate limited. Applies at once, kept across restarts. /wt provider", x + 24, y, descFont,
        LEFT_W - 24)
    y = y - TextHeight(note, 22) - 4

    y = AddSection("Names", x, y - 6)
    y = AddOption(x, y, "Translate player names in chat",
        "Chinese sender names are shown in English. Display only: clicking a name still whispers, invites or " ..
        "targets the real player. A new name shows untranslated on its first message.",
        function() return db().translateNames end, function(v) WT.Set("translateNames", v) end)
    y = AddOption(x + 18, y, "Keep the original name",
        "[风之子 | Son Of The Wind] instead of [Son Of The Wind]. /wt names both | english",
        function() return db().nameStyle == "both" end,
        function(v) WT.Set("nameStyle", v and "both" or "english") end)
    y = AddOption(x + 18, y, "Also in system lines",
        "\"丶喀秋莎 has come online / has gone offline\" and other lines the game writes with a name. /wt sysnames",
        function() return db().translateSystemNames end, function(v) WT.Set("translateSystemNames", v) end)
    y = AddOption(x + 18, y, "Also in player tooltips",
        "Mouse over a player: name, guild and guild rank in English. /wt tooltip",
        function() return db().translateTooltips end, function(v) WT.Set("translateTooltips", v) end)
    y = AddOption(x + 18, y, "Also in the friends, who and guild lists",
        "Names in the friends, who, ignore and guild lists (and guild ranks) in English. Display only: whisper, invite and the " ..
        "right-click menu still use the real name; the menu title shows the English. /wt social",
        function() return db().translateSocial end, function(v) WT.SetTranslateSocial(v) end)
    y = AddOption(x, y, "Translate channel names",
        "[5. 世界] is shown as [5. World]. Display only: /5 still talks in the same channel. /wt channels",
        function() return db().translateChannels end, function(v) WT.Set("translateChannels", v) end)

    y = AddSection("Show the original", x, y - 6)
    y = AddOption(x, y, "On incoming messages",
        "English (中文): handy to check a translation. /wt orig in toggle",
        function() return db().showOriginalIn end, function(v) WT.Set("showOriginalIn", v) end)
    y = AddOption(x, y, "On my own messages",
        "You always see what you typed; this adds the Chinese that was actually sent. /wt orig out toggle",
        function() return db().showOriginalOut end, function(v) WT.Set("showOriginalOut", v) end)

    y = AddSection("Display", x, y - 6)
    y = AddOption(x, y, "Same font and size for Chinese and English",
        "The game draws any chat line with Chinese in it in a bigger font. This uses one font (Nowar WarSans) " ..
        "for both, at your chat font size. /wt font",
        function() return db().unifyChatFont end, function(v) WT.SetUnifyChatFont(v) end)
    y = AddOption(x, y, "Show the OUT button",
        "Click it to switch outgoing translation. Shift-drag to move it. /wt button",
        function() return db().showOutButton end, function(v) WT.Set("showOutButton", v) end)
    y = AddOption(x, y, "Show the minimap button",
        "Opens this window. Drag it around the minimap.",
        function() return not db().minimap.hide end, function(v) WT.SetMinimapShown(v) end)
    return y
end

local function BuildRight()
    local db = DB
    local y = -4
    y = AddSection("What gets translated", RIGHT_X, y)
    local note = AddText(content, "Incoming = what others write to you. Outgoing = what you write.",
        RIGHT_X, y, descFont, WIDTH - RIGHT_X - 50)
    y = y - TextHeight(note, 12) - 6

    AddText(content, "Chat", RIGHT_X + 8, y, "GameFontNormalSmall")
    AddText(content, "In", COL_IN + 4, y, "GameFontNormalSmall")
    AddText(content, "Out", COL_OUT + 1, y, "GameFontNormalSmall")
    y = y - 16
    for _, t in ipairs(WT.CHAT_TYPES) do
        local code = t.code
        AddText(content, t.label, RIGHT_X + 8, y - 4, rowFont)
        AddTableCheck(COL_IN, y,
            function() return db().inTypes[code] end,
            function(v) db().inTypes[code] = v; WT.PushConfig() end)
        if not t.incomingOnly then
            AddTableCheck(COL_OUT, y,
                function() return db().outTypes[code] end,
                function(v) db().outTypes[code] = v; WT.PushConfig() end)
        end
        y = y - 20
    end

    y = AddSection("Channels you are in", RIGHT_X, y - 8)
    local hint = AddText(content, "Only used while \"Channels\" above is ticked.", RIGHT_X, y, descFont,
        WIDTH - RIGHT_X - 50)
    channelTop = y - TextHeight(hint, 12) - 4
end

local function Create()
    frame = CreateFrame("Frame", "WoWTranslateConfigFrame", UIParent, "BasicFrameTemplateWithInset")
    frame:SetSize(WIDTH, HEIGHT)
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
    frame.status:SetPoint("TOPLEFT", 14, -30)
    frame.status:SetPoint("TOPRIGHT", -14, -30)
    frame.status:SetJustifyH("LEFT")

    local scroll = CreateFrame("ScrollFrame", "WoWTranslateConfigScroll", frame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 10, -48)
    scroll:SetPoint("BOTTOMRIGHT", -30, 42)
    content = CreateFrame("Frame", nil, scroll)
    content:SetSize(WIDTH - 44, 600)
    scroll:SetScrollChild(content)

    leftBottom = BuildLeft()
    BuildRight()

    local check = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    check:SetSize(110, 22)
    check:SetPoint("BOTTOMLEFT", 14, 12)
    check:SetText("Check proxy")
    check:SetScript("OnClick", function()
        WT.proxyVersion = nil
        frame.status:SetText("Checking...")
        WT.Hello()
    end)

    local status = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    status:SetSize(110, 22)
    status:SetPoint("LEFT", check, "RIGHT", 6, 0)
    status:SetText("Proxy status")
    status:SetScript("OnClick", function() WT.Send("?STATUS") end)

    local clear = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
    clear:SetSize(110, 22)
    clear:SetPoint("LEFT", status, "RIGHT", 6, 0)
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
