-- Minimap button: opens the settings. Drag to move it around the minimap.

local _, WT = ...

local button

local function UpdatePosition()
    local angle = math.rad(WoWTranslateDB.minimap.angle or 215)
    local radius = (Minimap:GetWidth() / 2) + 5
    button:ClearAllPoints()
    button:SetPoint("CENTER", Minimap, "CENTER", math.cos(angle) * radius, math.sin(angle) * radius)
end

local function OnDragUpdate()
    local mx, my = Minimap:GetCenter()
    local cx, cy = GetCursorPosition()
    local scale = Minimap:GetEffectiveScale()
    cx, cy = cx / scale, cy / scale
    WoWTranslateDB.minimap.angle = math.deg(math.atan2(cy - my, cx - mx))
    UpdatePosition()
end

local function Create()
    button = CreateFrame("Button", "WoWTranslateMinimapButton", Minimap)
    button:SetSize(31, 31)
    button:SetFrameStrata("MEDIUM")
    button:SetFrameLevel(8)
    button:RegisterForClicks("LeftButtonUp", "RightButtonUp")
    button:RegisterForDrag("LeftButton")
    button:SetHighlightTexture("Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight")

    local icon = button:CreateTexture(nil, "BACKGROUND")
    icon:SetTexture("Interface\\Icons\\INV_Misc_Note_02")
    icon:SetSize(20, 20)
    icon:SetPoint("TOPLEFT", 7, -5)
    icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)

    local border = button:CreateTexture(nil, "OVERLAY")
    border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder")
    border:SetSize(53, 53)
    border:SetPoint("TOPLEFT")

    button:SetScript("OnClick", function() WT.ToggleConfig() end)
    button:SetScript("OnDragStart", function(self) self:SetScript("OnUpdate", OnDragUpdate) end)
    button:SetScript("OnDragStop", function(self) self:SetScript("OnUpdate", nil) end)
    button:SetScript("OnEnter", function(self)
        GameTooltip:SetOwner(self, "ANCHOR_LEFT")
        GameTooltip:AddLine("WoWTranslate")
        GameTooltip:AddLine("Click: settings", 1, 1, 1)
        GameTooltip:AddLine("Drag: move", 0.7, 0.7, 0.7)
        GameTooltip:AddLine(WT.StatusText(), 1, 1, 1, true)
        GameTooltip:Show()
    end)
    button:SetScript("OnLeave", GameTooltip_Hide)

    UpdatePosition()
    if WoWTranslateDB.minimap.hide then button:Hide() end
end

function WT.SetMinimapShown(shown)
    WoWTranslateDB.minimap.hide = not shown
    if button then
        if shown then button:Show() else button:Hide() end
    end
end

WT.AddLoadedHook(Create)
