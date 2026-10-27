# Turtle spells that give hit chance, for the proxy's hit display (see LoadHitSpells in GameData.cs).
# The 1.14 client shows hit from ActivePlayer UiHitModifier/UiSpellHitModifier, which a vanilla server never sends,
# so the proxy adds them up from gear, set bonuses, talents and auras, using this list.
#   python hit-spells.py <Turtle Spell.dbc> <out.csv>
# Turtle Spell.dbc: extract from the Turtle client's MPQs (DBFilesClient\Spell.dbc, last patch wins).
import struct, sys

APPLY_AURA = (6, 35, 65)              # aura, area aura (party), area aura (raid)
MOD_HIT_CHANCE, MOD_SPELL_HIT_CHANCE = 54, 55
ALL_MAGIC = 0x7E                      # spell hit for one school only (e.g. Elemental Precision) is left out

dbc, out = sys.argv[1:3]
b = open(dbc, 'rb').read()
n, fields, size, _ = struct.unpack_from('<4i', b, 4)

with open(out, 'w', newline='') as f:
    f.write('SpellId,Hit,SpellHit\n')
    for i in range(n):
        r = struct.unpack_from(f'<{fields}i', b, 20 + i * size)
        hit = spell_hit = 0
        # 1.12 layout: Effect[3] at 61, EffectBaseDice[3] at 67, EffectBasePoints[3] at 76, EffectAura[3] at 91,
        # EffectMiscValue[3] at 106
        for e in range(3):
            if r[61 + e] not in APPLY_AURA:
                continue
            value = r[76 + e] + r[67 + e]
            if r[91 + e] == MOD_HIT_CHANCE:
                hit += value
            elif r[91 + e] == MOD_SPELL_HIT_CHANCE and (r[106 + e] == 0 or r[106 + e] & ALL_MAGIC == ALL_MAGIC):
                spell_hit += value
        if hit or spell_hit:
            f.write(f'{r[0]},{hit},{spell_hit}\n')
