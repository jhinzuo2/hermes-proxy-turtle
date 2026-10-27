-- "Transmogrified to:" on item tooltips through the Capycraft HermesProxy.
-- The client's port of the Turtle transmog UI draws that line only when a tooltip opens (its 0.2 s refresh drops
-- it) and never for inspected players. We draw it on every refresh, from the best source we have:
--   own gear:  the Turtle UI's TransmogStatus table / the server's "TransmogStatus:slot:item,..." (prefix
--              TW_TRANSMOG), else the proxy's guess from the look (S messages)
--   others:    their Turtle UI's answer to INSShowTransmogs (T messages, relayed by the proxy), else the
--              proxy's guess for server-made transmog copies (C messages)
-- Proxy messages (prefix HPTM, see UpdateHandler.cs in the proxy):
--   S|slot|equippedItemId|look name        own slot, 0-based (equippedItemId 0 = not transmogged)
--   C|copyItemId|look name                 a server-made transmog copy
--   T|player|slot|transmogId|equippedId    an inspected player's transmog (slot 1-based, "start" = new list)
-- An empty look name = a look this client has no item for. We send "I|player" to have the proxy ask a player.

local PREFIX = "HPTM"
local TURTLE_PREFIX = "TW_TRANSMOG"
local COLOR = "|cfff470f4" -- the client's own color for this line
local HEADER = TRANSMOG_CHANGED_TO or TRANSMOGRIFIED_HEADER or "Transmogrified to:"
local UNKNOWN = "(a look this client doesn't have)"

local ownGuess = {}   -- inventory slot (1-based) -> { item = id, name = look }
local ownServer = {}  -- inventory slot (1-based) -> transmog item id (from TransmogStatus messages)
local copies = {}     -- item id -> look
local others = {}     -- player name -> inventory slot -> transmog item id
local asked = {}      -- player name -> time we last asked

C_ChatInfo.RegisterAddonMessagePrefix(PREFIX)
C_ChatInfo.RegisterAddonMessagePrefix(TURTLE_PREFIX)

-- Puts our line into line 2 (where the client puts its own), replacing the client's; name nil = just remove it.
local function apply(tooltip, name)
  local line = _G[tooltip:GetName() .. "TextLeft2"]
  if not line or tooltip:NumLines() < 2 then return end
  local text = line:GetText() or ""
  local stripped = text:gsub("^|c%x%x%x%x%x%x%x%x" .. HEADER:gsub("%p", "%%%0") .. "\n.-\n|r", "")
  if name then
    stripped = COLOR .. HEADER .. "\n" .. (name ~= "" and name or UNKNOWN) .. "\n|r" .. stripped
  end
  if stripped ~= text then
    line:SetText(stripped)
    tooltip:Show()
  end
end

local function itemIdFromLink(link)
  return link and tonumber(link:match("item:(%d+)"))
end

-- Name of a transmog item from the server, nil while the client is still fetching it.
local function nameOf(itemId)
  if not itemId or itemId == 0 then return nil end
  return (GetItemInfo(itemId))
end

-- Look name of an own slot and whether we know anything about it (name nil + known = not transmogged).
local function ownLook(slot)
  local equipped = GetInventoryItemID("player", slot)
  if not equipped then return nil, false end
  local transmog = type(TransmogStatus) == "table" and TransmogStatus[equipped] or ownServer[slot]
  if transmog and transmog ~= 0 and transmog ~= equipped then
    local name = nameOf(transmog)
    if name then return name, true end
  end
  local guess = ownGuess[slot]
  if guess and (guess.item == 0 or guess.item == equipped) then
    if guess.item == 0 then return nil, true end
    return guess.name, true
  end
  return nil, false
end

local function hook(tooltip)
  hooksecurefunc(tooltip, "SetInventoryItem", function(self, unit, slot)
    if not unit or not slot then return end
    if UnitIsUnit(unit, "player") then
      local name, known = ownLook(slot)
      if known then apply(self, name) end
    else
      local list = others[UnitName(unit) or ""]
      local name = list and nameOf(list[slot])
      if not name then name = copies[GetInventoryItemID(unit, slot) or 0] end
      if name then apply(self, name) end
    end
  end)
  tooltip:HookScript("OnTooltipSetItem", function(self)
    local _, link = self:GetItem()
    local look = copies[itemIdFromLink(link) or 0]
    if look then apply(self, look) end
  end)
end

hook(GameTooltip)
if ItemRefTooltip then hook(ItemRefTooltip) end

local function toProxy(text)
  C_ChatInfo.SendAddonMessage(PREFIX, text, "WHISPER", UnitName("player"))
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("CHAT_MSG_ADDON")
frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("INSPECT_READY")
frame:SetScript("OnEvent", function(_, event, prefix, text)
  if event == "PLAYER_ENTERING_WORLD" then
    toProxy("?") -- resend what we missed while loading
    -- the real transmogs of our own gear; the Turtle UI asks too, but only ~10 s later
    if IsInGuild() then C_ChatInfo.SendAddonMessage(TURTLE_PREFIX, "GetTransmogStatus", "GUILD") end
    return
  end
  if event == "INSPECT_READY" then
    local name = select(6, GetPlayerInfoByGUID(prefix or ""))
    if name and name ~= UnitName("player") and (not asked[name] or GetTime() - asked[name] > 5) then
      asked[name] = GetTime()
      toProxy("I|" .. name)
    end
    return
  end
  if not text then return end
  if prefix == TURTLE_PREFIX then
    local status = text:match("^TransmogStatus:(.*)")
    if status then
      wipe(ownServer)
      for slot, item in status:gmatch("(%d+):(%d+)") do
        ownServer[tonumber(slot)] = tonumber(item)
        nameOf(tonumber(item)) -- fetch the name now
      end
    end
    return
  end
  if prefix ~= PREFIX then return end
  local kind, a, b, c = strsplit("|", text, 5)
  if kind == "S" then
    ownGuess[(tonumber(a) or -1) + 1] = { item = tonumber(b) or 0, name = c or "" }
  elseif kind == "C" then
    copies[tonumber(a) or 0] = b or ""
  elseif kind == "T" and a then
    if b == "start" then
      others[a] = {}
    else
      others[a] = others[a] or {}
      others[a][tonumber(b) or 0] = tonumber(c)
      nameOf(tonumber(c))
    end
  end
end)
