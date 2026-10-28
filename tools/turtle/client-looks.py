# Item -> look table of the 1.14 client, from its own DB2s (dumped with `casc <client dir> table <fdid> <out.csv>`):
#   Item 841626, ItemModifiedAppearance 982457, ItemAppearance 982462.
# The proxy's item CSVs are stock HermesProxy and miss the items Ravencraft added (e.g. 55277 End of All Ambitions),
# so a Capycraft transmog copy with such a look had no client item to stand in for it and was drawn invisible.
# Only gear in a visible slot whose client display id is the one the server uses (tortoise-wow item_template): for most
# items the two numberings differ, so a look is kept only where they agree.
#   python client-looks.py <t841626.csv> <t982457.csv> <t982462.csv> <tw_world_item_template.sql> <out ClientItemLooks1.csv>
import csv, re, sys

item_path, ima_path, appearance_path, sql_path, out_path = sys.argv[1:6]

VISIBLE_SLOTS = {1, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 25, 26}
server_display = {}
with open(sql_path, encoding='utf-8', errors='replace') as f:
    # (entry, class, subclass, 'name', 'description', display_id, ...
    for m in re.finditer(r"\((\d+),\d+,\d+,'(?:[^'\\]|''|\\.)*','(?:[^'\\]|''|\\.)*',(\d+)", f.read()):
        server_display[int(m.group(1))] = int(m.group(2))

inventory_type = {}
with open(item_path, newline='') as f:
    r = csv.reader(f)
    next(r)
    for row in r:
        inventory_type[int(row[0])] = int(row[4])  # ID, ClassID, SubclassID, Material, InventoryType

display = {}
with open(appearance_path, newline='') as f:
    r = csv.reader(f)
    next(r)
    for row in r:
        display[int(row[0])] = int(row[2])  # ID, DisplayType, ItemDisplayInfoID, DefaultIconFileDataID, UiOrder

# ID, ID, ItemID, ItemAppearanceModifierID, ItemAppearanceID, OrderIndex, TransmogSourceTypeEnum, ItemID (relation)
looks = {}
with open(ima_path, newline='') as f:
    r = csv.reader(f)
    next(r)
    for row in r:
        item, modifier, appearance = int(row[2]), int(row[3]), int(row[4])
        if inventory_type.get(item) not in VISIBLE_SLOTS or display.get(appearance, 0) == 0:
            continue
        if item not in looks or modifier < looks[item][0]:
            looks[item] = (modifier, display[appearance])
looks = {item: look for item, look in looks.items() if server_display.get(item) == look[1]}

with open(out_path, 'w', newline='') as f:
    w = csv.writer(f)
    w.writerow(['ItemID', 'DisplayID', 'InventoryType'])
    for item in sorted(looks):
        w.writerow([item, looks[item][1], inventory_type[item]])
print(f'{len(looks)} client items with a look')
