-- BoatClock: stand still on a ship and it logs when you start / stop being carried, plus loading screens and
-- zone changes, stamped with the PC clock. Run it on a 1.12 (Turtle 1.18.1) and a 1.14 client side by side
-- to compare a transport's timing. Same file for both clients (Lua 5.0 safe).
-- /boatclock        show the saved log
-- /boatclock clear  empty it
BoatClockLog = BoatClockLog or {}

local client = C_Map and "1.14" or "1.12"
local frame = CreateFrame("Frame")
local lastX, lastY
local moving = false
local movedAt, stillSince = 0, 0
local unknownLogged = false
local tick = 0

local function Log(what)
    local line = date("%H:%M:%S") .. " [" .. client .. "] " .. what .. " - " .. (GetZoneText() or "?")
    DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99BoatClock|r " .. line)
    table.insert(BoatClockLog, line)
end

local function Position()
    if C_Map and C_Map.GetBestMapForUnit then
        local map = C_Map.GetBestMapForUnit("player")
        local pos = map and C_Map.GetPlayerMapPosition(map, "player")
        if pos then return pos.x, pos.y end
        return nil
    end
    if WorldMapFrame and WorldMapFrame:IsShown() then return lastX, lastY end
    SetMapToCurrentZone()
    local x, y = GetPlayerMapPosition("player")
    if x == 0 and y == 0 then return nil end
    return x, y
end

frame:SetScript("OnUpdate", function(a, b)
    tick = tick + (b or arg1 or 0)
    if tick < 0.25 then return end
    tick = 0
    local now = GetTime()
    local x, y = Position()
    if not x then
        if not unknownLogged then Log("position unknown (open sea?)") end
        unknownLogged = true
        return
    end
    unknownLogged = false
    if lastX then
        local d = math.abs(x - lastX) + math.abs(y - lastY)
        if d > 0.00002 then
            if not moving and now - stillSince >= 3 then Log("carried: STARTED") end
            moving = true
            movedAt = now
        elseif moving and now - movedAt >= 2 then
            moving = false
            stillSince = now
            Log("carried: STOPPED")
        elseif not moving and stillSince == 0 then
            stillSince = now
        end
    end
    lastX, lastY = x, y
end)

frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("ZONE_CHANGED_NEW_AREA")
frame:SetScript("OnEvent", function(a, b)
    local ev = b or event
    lastX, lastY = nil, nil
    if ev == "PLAYER_ENTERING_WORLD" then Log("loading screen done") else Log("zone change") end
end)

SLASH_BOATCLOCK1 = "/boatclock"
SlashCmdList["BOATCLOCK"] = function(msg)
    if msg == "clear" then
        BoatClockLog = {}
        DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99BoatClock|r log cleared")
        return
    end
    for i = 1, table.getn(BoatClockLog) do DEFAULT_CHAT_FRAME:AddMessage(BoatClockLog[i]) end
end
