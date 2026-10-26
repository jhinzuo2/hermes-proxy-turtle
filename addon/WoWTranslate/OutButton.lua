-- The on-screen OUT button: click to switch outgoing EN->CN translation on or off.
-- Shift-drag to move it.

local _, WT = ...

local button

local function Refresh()
    if not button then return end
    local db = WoWTranslateDB
    if db.showOutButton then button:Show() else button:Hide() end

    local on = db.enabled and db.outgoing
    if on then
        button:SetText("|cff55ff55OUT: ON|r")
    elseif db.outgoing then
        button:SetText("|cffaaaaaaOUT: ON|r") -- outgoing set, but all translation is off
    else
        button:SetText("|cffff5555OUT: OFF|r")
    end
end

local function SavePosition()
    local point, _, relativePoint, x, y = button:GetPoint(1)
    WoWTranslateDB.outButton = { point, relativePoint, x, y }
end

local function Create()
    button = CreateFrame("Button", "WoWTranslateOutButton", UIParent, "UIPanelButtonTemplate")
    button:SetSize(78, 20)
    button:SetFrameStrata("MEDIUM")
    button:SetClampedToScreen(true)
    button:SetMovable(true)
    button:RegisterForDrag("LeftButton")
    button:RegisterForClicks("LeftButtonUp")

    local saved = WoWTranslateDB.outButton
    if saved then
        button:SetPoint(saved[1], UIParent, saved[2], saved[3], saved[4])
    else
        button:SetPoint("BOTTOMLEFT", ChatFrame1, "TOPLEFT", 0, 28)
    end

    button:SetScript("OnClick", function()
        WT.SetOutgoing(not WoWTranslateDB.outgoing)
    end)
    button:SetScript("OnDragStart", function(self)
        if IsShiftKeyDown() then self:StartMoving() end
    end)
    button:SetScript("OnDragStop", function(self)
        self:StopMovingOrSizing()
        SavePosition()
    end)
    button:SetScript("OnEnter", function(self)
        GameTooltip:SetOwner(self, "ANCHOR_TOP")
        GameTooltip:AddLine("WoWTranslate")
        GameTooltip:AddLine("Click: outgoing English -> Chinese on/off", 1, 1, 1)
        GameTooltip:AddLine("Shift-drag: move", 0.7, 0.7, 0.7)
        GameTooltip:AddLine(WT.StatusText(), 1, 1, 1, true)
        GameTooltip:Show()
    end)
    button:SetScript("OnLeave", GameTooltip_Hide)

    Refresh()
end

WT.AddLoadedHook(Create)
WT.AddStateHook(Refresh)
