using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Client
{
    public partial class WorldClient
    {
        // The 1.14 client shows hit chance (GetHitModifier/GetSpellHitModifier, the character sheet, addons like ECS)
        // from ActivePlayer UiHitModifier/UiSpellHitModifier. A vanilla server has no such fields and never sends the
        // hidden passive auras that give hit, so the client showed 0%. Add it up the way the server does: equip
        // spells of the gear we wear, set bonuses we have the pieces for, passive spells we know (talents) and our
        // auras, with each spell's hit from Turtle's Spell.dbc (GameData.HitSpells). Display only: the server still
        // decides every hit and miss.
        readonly HashSet<uint> _knownHitSpells = new();
        float? _sentHit;
        float? _sentSpellHit;

        void NoteKnownSpells(IEnumerable<uint> learned, IEnumerable<uint> removed)
        {
            bool changed = false;
            if (removed != null)
                foreach (uint spellId in removed)
                    changed |= _knownHitSpells.Remove(spellId);
            if (learned != null)
                foreach (uint spellId in learned)
                    if (GameData.HitSpells.ContainsKey(spellId))
                        changed |= _knownHitSpells.Add(spellId);
            if (changed)
                UpdateHitDisplay();
        }

        void ForgetKnownSpells()
        {
            _knownHitSpells.Clear();
            _sentHit = null;
            _sentSpellHit = null;
        }

        void UpdateHitDisplay()
        {
            var state = GetSession().GameState;
            if (state.CurrentPlayerGuid == null || GameData.HitSpells.Count == 0)
                return;
            var player = state.GetCachedObjectFieldsLegacy(state.CurrentPlayerGuid);
            if (player == null)
                return; // not in the world yet

            int hit = 0, spellHit = 0;
            void Add(uint spellId)
            {
                if (GameData.HitSpells.TryGetValue(spellId, out var value))
                {
                    hit += value.Hit;
                    spellHit += value.SpellHit;
                }
            }

            foreach (uint spellId in _knownHitSpells)
                Add(spellId);

            int OBJECT_FIELD_ENTRY = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_ENTRY);
            var setPieces = new Dictionary<uint, int>();
            for (int slot = 0; slot < 19; slot++)
            {
                WowGuid64 itemGuid = state.GetInventorySlotItem(slot);
                if (itemGuid.IsEmpty())
                    continue;
                var itemFields = state.GetCachedObjectFieldsLegacy(itemGuid.To128(state));
                if (itemFields == null || !itemFields.ContainsKey(OBJECT_FIELD_ENTRY))
                    continue;
                ItemTemplate item = GameData.GetItemTemplate(itemFields[OBJECT_FIELD_ENTRY].UInt32Value);
                if (item == null)
                    continue; // added when its answer comes (HandleItemQueryResponse)
                for (int i = 0; i < item.TriggeredSpellIds.Length; i++)
                    if (item.TriggeredSpellTypes[i] == 1 && item.TriggeredSpellIds[i] > 0) // on equip
                        Add((uint)item.TriggeredSpellIds[i]);
                if (item.ItemSet != 0)
                    setPieces[item.ItemSet] = setPieces.GetValueOrDefault(item.ItemSet) + 1;
            }
            foreach (var set in setPieces)
                if (GameData.ItemSetBonuses.TryGetValue(set.Key, out var bonuses))
                    foreach (var bonus in bonuses)
                        if (set.Value >= bonus.Threshold)
                            Add(bonus.SpellId);

            int UNIT_FIELD_AURA = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_AURA);
            if (UNIT_FIELD_AURA >= 0)
                for (int i = 0; i < 48; i++)
                    if (player.TryGetValue(UNIT_FIELD_AURA + i, out var aura) && aura.UInt32Value != 0)
                        Add(aura.UInt32Value);

            float meleeHit = Math.Max(0, hit);
            float magicHit = Math.Max(0, spellHit);
            if (_sentHit == meleeHit && _sentSpellHit == magicHit)
                return;
            _sentHit = meleeHit;
            _sentSpellHit = magicHit;
            Log.Print(LogType.Storage, $"[HitDisplay] hit {meleeHit}% spell hit {magicHit}%");

            UpdateObject updatePacket = new UpdateObject(state);
            ObjectUpdate updateData = new ObjectUpdate(state.CurrentPlayerGuid, UpdateTypeModern.Values, GetSession());
            updateData.ActivePlayerData.UiHitModifier = meleeHit;
            updateData.ActivePlayerData.UiSpellHitModifier = magicHit;
            updatePacket.ObjectUpdates.Add(updateData);
            SendPacketToClient(updatePacket);
        }
    }
}
