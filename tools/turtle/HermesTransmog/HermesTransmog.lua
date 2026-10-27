-- "Transmogrified to:" on item tooltips through the Capycraft HermesProxy.
-- The 1.14 client draws that line only on the first frame of a tooltip (its 0.2 s refresh drops it), takes the
-- look from the slot's visible item (wrong for looks it has no item for) and never draws it for inspected players.
-- The proxy tells us the looks over addon messages (prefix HPTM, see UpdateHandler.cs in the proxy):
--   S|slot|equippedItemId|look name   our own slot (0-based; equippedItemId 0 = not transmogged)
--   C|copyItemId|look name            a server-made transmog copy (other players' gear)
-- An empty look name = a look this client has no item for.

local PREFIX = "HPTM"
local COLOR = "|cfff470f4" -- the client's own color for this line
local HEADER = TRANSMOGRIFIED_HEADER or "Transmogrified to:"
local UNKNOWN = "(a look this client doesn't have)"

local own = {}    -- inventory slot (1-based) -> { item = id, name = look }
local copies = {} -- item id -> look

C_ChatInfo.RegisterAddonMessagePrefix(PREFIX)

-- Puts our line into line 2 (where the client puts its own), replacing the client's; name nil = just remove it.
local function apply(tooltip, name)
  local line = _G[tooltip:GetName() .. "TextLeft2"]
  if not line or tooltip:NumLines() < 2 then return end
  local text = line:GetText() or ""
  local stripped = text:gsub("^|cfff470f4.-\n.-\n|r", "")
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

local function hook(tooltip)
  hooksecurefunc(tooltip, "SetInventoryItem", function(self, unit, slot)
    if not unit or not slot then return end
    if UnitIsUnit(unit, "player") then
      local entry = own[slot]
      if not entry then return end
      if entry.item == 0 then
        apply(self, nil)
      elseif GetInventoryItemID("player", slot) == entry.item then
        apply(self, entry.name)
      end
    else
      local look = copies[GetInventoryItemID(unit, slot) or 0]
      if look then apply(self, look) end
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

local frame = CreateFrame("Frame")
frame:RegisterEvent("CHAT_MSG_ADDON")
frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:SetScript("OnEvent", function(_, event, prefix, text)
  if event == "PLAYER_ENTERING_WORLD" then
    C_ChatInfo.SendAddonMessage(PREFIX, "?", "WHISPER", UnitName("player")) -- resend what we missed while loading
    return
  end
  if prefix ~= PREFIX or not text then return end
  local kind, a, b, c = strsplit("|", text, 4)
  if kind == "S" then
    own[(tonumber(a) or -1) + 1] = { item = tonumber(b) or 0, name = c or "" }
  elseif kind == "C" then
    copies[tonumber(a) or 0] = b or ""
  end
end)
