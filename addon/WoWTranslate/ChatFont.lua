-- Same font and size for Chinese and English in chat.
--
-- The client's chat font is a family with one font per alphabet, each with its own size. Any line
-- with a Chinese character is drawn whole in the CJK member, which is much bigger than the chosen
-- chat size. ChatFont.xml defines families where every alphabet uses Nowar WarSans (Latin + CJK,
-- WoW look) at the same size; this applies the one matching each chat window's size setting.

local _, WT = ...

local FONT = "Interface\\AddOns\\WoWTranslate\\Fonts\\WarSansTT-Bliz-500.ttf"
local MIN_SIZE, MAX_SIZE = 8, 32

local function WindowSize(frame)
    local _, size = FCF_GetChatWindowInfo(frame:GetID())
    size = math.floor((size or 14) + 0.5)
    return math.max(MIN_SIZE, math.min(MAX_SIZE, size))
end

local function Apply(frame)
    if not (frame and frame.SetFontObject) or not WoWTranslateDB.unifyChatFont then return end
    local size = WindowSize(frame)
    local family = _G["WoWTranslateChatFont" .. size]
    if family then
        frame:SetFontObject(family)
    else
        frame:SetFont(FONT, size, "")
    end
    frame:SetJustifyH("LEFT") -- else the chat may come out centered until a /reload
end

function WT.ApplyChatFonts()
    for i = 1, NUM_CHAT_WINDOWS do
        Apply(_G["ChatFrame" .. i])
    end
end

function WT.SetUnifyChatFont(on)
    WoWTranslateDB.unifyChatFont = on
    if on then
        WT.ApplyChatFonts()
        return
    end
    -- Back to the client's own chat font at each window's size.
    for i = 1, NUM_CHAT_WINDOWS do
        local frame = _G["ChatFrame" .. i]
        if frame then
            local size = WindowSize(frame)
            frame:SetFontObject(ChatFontNormal)
            FCF_SetChatWindowFontSize(nil, frame, size)
        end
    end
end

WT.AddLoadedHook(function()
    -- Chat window font size menu (and anything else that resizes a window's font).
    hooksecurefunc("FCF_SetChatWindowFontSize", function(_, chatFrame)
        Apply(chatFrame or FCF_GetCurrentChatFrame())
    end)
    -- New whisper/temporary windows pick up their font when opened.
    if FCF_OpenTemporaryWindow then
        hooksecurefunc("FCF_OpenTemporaryWindow", function()
            C_Timer.After(0, WT.ApplyChatFonts)
        end)
    end

    local events = CreateFrame("Frame")
    events:RegisterEvent("PLAYER_ENTERING_WORLD")
    events:RegisterEvent("UPDATE_CHAT_WINDOWS")
    events:SetScript("OnEvent", function()
        -- After Blizzard's own chat setup on the same event.
        C_Timer.After(0, WT.ApplyChatFonts)
    end)
end)
