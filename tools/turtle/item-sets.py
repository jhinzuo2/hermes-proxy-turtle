# Turtle's item sets as CSVs for the proxy's ItemSet/ItemSetSpell hotfixes (see LoadItemSetHotfixes in GameData.cs).
# The 1.14 client knows 172 sets; Turtle has 359 (alternative tier sets, custom crafted/PvP sets) and changed the
# bonuses of many of the others, so the proxy sends all of Turtle's sets.
#   python item-sets.py <Turtle ItemSet.dbc> <client ItemSetSpell.csv> <out dir>
# Turtle ItemSet.dbc: extract from the Turtle client's MPQs (DBFilesClient\ItemSet.dbc, last patch wins).
# Client ItemSetSpell.csv: casc <client dir> table 1314689 <csv> (ID,ChrSpecID,SpellID,Threshold,ItemSetID); only the
# ID and ItemSetID columns are used, so the proxy can overwrite or remove the client's own bonus rows of a set.
import csv, os, struct, sys

dbc, client_csv, out = sys.argv[1:4]
b = open(dbc, 'rb').read()
n, fields, size, _ = struct.unpack_from('<4i', b, 4)
strings = 20 + n * size

def text(offset):
    end = b.index(b'\0', strings + offset)
    return b[strings + offset:end].decode('utf-8')

with open(os.path.join(out, 'ItemSet1.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['ID', 'Name', 'RequiredSkill', 'RequiredSkillRank'] + [f'Item{i}' for i in range(17)] +
               [f'Spell{i}' for i in range(8)] + [f'Threshold{i}' for i in range(8)])
    # 1.12 layout: ID, Name[8] + flags, ItemID[17], SpellID[8], Threshold[8], RequiredSkill, RequiredSkillRank
    for r in sorted((struct.unpack_from(f'<{fields}I', b, 20 + i * size) for i in range(n)), key=lambda r: r[0]):
        w.writerow([r[0], text(r[1]), r[43], r[44]] + list(r[10:27]) + list(r[27:35]) + list(r[35:43]))

with open(client_csv, encoding='utf-8') as f:
    rows = list(csv.reader(f))[1:]
with open(os.path.join(out, 'ItemSetSpellClient1.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['ID', 'ItemSetID'])
    for r in sorted(rows, key=lambda r: int(r[0])):
        w.writerow([r[0], r[-1]])
