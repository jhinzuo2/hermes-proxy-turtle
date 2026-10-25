# Tortoise-WoW world data vs. client model lists
base files: 190, update files: 150
  creature_template: 14341 rows
  gameobject_template: 21196 rows
  item_template: 26544 rows
  creature_equip_template: 5992 rows
  creature: 92551 rows
  gameobject: 65146 rows
statements: bad_tuple=6, creature_equip_template.delete=1, creature_equip_template.insert=6011, creature_template.insert=27092, creature_template.update=932, creature.delete=23, creature.insert=98795, creature.update=4108, gameobject_template.insert=42029, gameobject.delete=8, gameobject.insert=67211, gameobject.update=2999, item_template.insert=45683, item_template.update=1
unhandled statements touching model columns: 0

## A. NPC models missing from the client list: 57 displays, 38 of them spawned in the world (469 spawns)
display | spawns | npcs (entry name)
21578 | 99 | 6202 Legashi Hellcaller; 11791 Putridus Trickster; 62812 Foulheart Darkweaver (+3)
21579 | 91 | 3757 Xavian Hellcaller; 11451 Wildspawn Satyr; 11790 Putridus Satyr (+1)
21580 | 63 | 4674 Hatefury Shadowstalker; 6126 Haldarr Trickster
21577 | 60 | 3770 Bleakheart Shadowstalker; 92126 Blacktalon Corruptor; 92123 Blacktalon Trickster
21600 | 22 | 62777 Deathtotem Avenger
21599 | 20 | 62774 Deathtotem Bonespeaker
21581 | 19 | 6127 Haldarr Felsworn; 11453 Wildspawn Trickster
18934 | 15 | 60939 Deserter Exile
18935 | 15 | 60939 Deserter Exile
21601 | 14 | 62773 Deathtotem Shaman
18936 | 12 | 60940 Deserter Turncoat
18226 | 4 | 80230 High Elf Ranger; 62962 Hellador Swiftluck
19146 | 3 | 61261 Ravenshire Guard
7010 | 2 | 7826 Ambassador Ardalan; 14387 Lothos Riftwaker
18098 | 2 | 40021 Tiki; 9940 The Peach King
18225 | 2 | 80459 Alinerenah Sunsparrow; 60877 Ansela Dawnshield
18101 | 2 | 80871 Warden Watcher
18230 | 2 | 80976 Knight's Charger; 90976 Armored Knight's Warhorse
21071 | 2 | 62492 Mangled Corpse
21597 | 2 | 63182 Archaeologist Evenpike
4245 | 1 | 3339 Captain Thalo'thas Brightsun; 50636 Drake
13150 | 1 | 13085 Myrokos Silentform
18727 | 1 | 11500 Ganum Highmountain
18131 | 1 | 80875 Illidari Naga Siren
18710 | 1 | 60451 Jessie Sugar-Tongue
18975 | 1 | 60928 Kelly Vaulstone
18976 | 1 | 60929 Petty Officer Milldough
18977 | 1 | 60930 Quartermaster Alden
18974 | 1 | 60938 Deckmaster Javin
18937 | 1 | 60941 Sellick Voss
18152 | 1 | 61189 Mulin Snowbeard
20549 | 1 | 61814 Edrion Ironstrike
21192 | 1 | 62715 Arkod the White
21224 | 1 | 62718 Drethanis Mournshade
21225 | 1 | 62725 Thromrar Darkflame
21598 | 1 | 62785 Champion Rotag
21477 | 1 | 62981 Lotka Muddoll
21226 | 1 | 62682 Assistant Steamflare

## B. GameObjects: 11938 templates dropped by the proxy (entry >= 1000000), 34 use a WMO model (flag now applied), 50 transports.
Custom-range objects (entry 211001-999999, non-WMO, spawned) the proxy still sends: 30 displays, 211 spawns. No client GO model list exists, so these are unverified; 'seen' = rendered in our logs.
display | spawns | seen | objects (entry name type)
6405 | 42 | seen | 300203 Apple Bob t3
3651 | 42 | - | 300608 Azureshimmer Feather t3
36 | 38 | seen | 987658 Equipment Crates t5
6663 | 28 | seen | 223363 Blood Ring Banner t5
6151 | 10 | seen | 211062 Elwynn Fence t5
4176 | 9 | seen | 211022 Commander's Tent t5
1387 | 6 | - | 300400 Gordok Tribute t3; 300401 Gordok Tribute t3; 300402 Gordok Tribute t3 (+4)
5 | 5 | seen | 987654 Gri'lek, with the Iron Blood t2; 987655 Hazza'rah, the Dreamweaver t2; 987656 Renataki, of the Thousand Blades t2 (+1)
25790 | 4 | - | 300602 Brazier of Corruption t2; 300603 Brazier of Defilement t2; 300604 Brazier of Decay t2 (+1)
33095 | 3 | - | 300525 DalaranDalaran_Painting_12.mdx t5
23433 | 2 | - | 379545 Half-Buried Treasure Chest t3
5495 | 2 | seen | 379546 Meeting Stone t23; 379547 Meeting Stone t23
33094 | 2 | - | 300524 DalaranDalaran_Painting_11.mdx t5
33096 | 2 | - | 300526 DalaranDalaran_Painting_13.mdx t5
4713 | 1 | - | 211035 Lair Exit t5
134 | 1 | - | 211064 Rockwall Fence t5
6434 | 1 | - | 300057 Swirling Maelstrom t8
4673 | 1 | - | 300601 Door t0
318 | 1 | - | 300000 Gnomish Toolbox t3
5494 | 1 | seen | 379548 Meeting Stone t23
33088 | 1 | - | 300518 NEWSTYLEVLADLAMPSTREET.mdx t5
33090 | 1 | - | 300520 SUNWELLPASSIVEDOODADSSUNWELLSunwellRaid_Gate_02.mdx t5
33091 | 1 | - | 300521 SUNWELLPASSIVEDOODADSSUNWELLSunwellRaid_Gate_03.mdx t5
6448 | 1 | - | 300530 Winterax Supplies t3
163 | 1 | seen | 300531 Stolen Weapon Plans t3
499 | 1 | - | 300532 Human Tombstone t3
8640 | 1 | - | 223364 Bloodstone t5
24081 | 1 | seen | 300421 Forgewright's Brazier t2
25435 | 1 | - | 300606 Barrier of Ursol t0; 300611 Barrier of Peroth'arn t0
404 | 1 | - | 300609 Relic of Elunaris t3

## C1. Wearable items the client doesn't know whose display has no client model: 260 items, 236 displays
(these show no model when worn; if the proxy pushed appearance records for them they would dangle)
33247 | 6 items | 281044 Moonheart Belt; 1634105 Grasp of the Old God; 1784249 Plagueheart Sash
7284 | 4 items | 1447081 Stormrage Bracers; 1634107 Bindings of Faith; 1927740 Earthshatter Bracelets
2539 | 3 items | 280590 Strategist's Dragonhide Epaulets; 1892919 Mantle of Pestilence; 1983618 Netherwind Epaulets
40194 | 3 items | 1982860 Judgement Helm; 1756154 Redemption Helm; 1966257 Redemption Helmet
21333 | 3 items | 1814423 Guild Tabard; 1927291 Guild Tabard; 1963148 Guild Tabard
3450 | 3 items | 1899481 Judgement Chestguard; 1971301 Earthshatter Breastplate; 1981843 Vest of Encroaching Darkness
66554 | 2 items | 70515 Lawbringer Greaves; 1968915 Redemption Greaves
66587 | 2 items | 70523 Judgement Legguards; 1915084 Redemption Legguards
66517 | 2 items | 70687 Crown of Wrath; 70689 Chestplate of Wrath
33701 | 2 items | 70826 Stormcaller's Leggings; 70831 Stormcaller's Pants
40017 | 2 items | 1245747 Guild Tabard; 1956206 Tabard of the Immortal Guardian
41442 | 2 items | 1899483 Judgement Legguards; 1901787 Leggings of the Talon
14511 | 2 items | 1927738 Earthshatter Leggings; 1983863 Slime-coated Leggings
16820 | 2 items | 1981142 Boots of Wrath; 1983411 Netherwind Slippers
61077 | 1 items | 83044 Kazgrim Test Orc Belt 4
61088 | 1 items | 83054 Kazgrim Test Troll Feather Pants
66574 | 1 items | 70500 Lawbringer Crown
66575 | 1 items | 70501 Lawbringer Pauldrons
66573 | 1 items | 70502 Lawbringer Chestplate
66577 | 1 items | 70503 Lawbringer Bindings
66576 | 1 items | 70504 Lawbringer Girdle
66579 | 1 items | 70506 Lawbringer Leggings
66578 | 1 items | 70507 Lawbringer Sabatons
66580 | 1 items | 70508 Lawbringer Gauntlets
66550 | 1 items | 70509 Lawbringer Helmet
66551 | 1 items | 70510 Lawbringer Shoulderguards
66549 | 1 items | 70511 Lawbringer Chestguard
66553 | 1 items | 70512 Lawbringer Wristguards
66552 | 1 items | 70513 Lawbringer Waistguard
66555 | 1 items | 70514 Lawbringer Legguards
66556 | 1 items | 70516 Lawbringer Handguards
66582 | 1 items | 70517 Judgement Helmet
66583 | 1 items | 70518 Judgement Shoulderguards
66581 | 1 items | 70519 Judgement Chestguard
66585 | 1 items | 70520 Judgement Wristguards
66588 | 1 items | 70521 Judgement Handguards
66584 | 1 items | 70522 Judgement Waistguard
66586 | 1 items | 70524 Judgement Greaves
66598 | 1 items | 70525 Judgement Crown
66599 | 1 items | 70526 Judgement Pauldrons

## C2. Items whose icon can't be resolved (show '?'): 297 items (255 of them client-known custom items), 269 displays
  known '?' item: 61000 Time-Worn Rune display 69118
  known '?' item: 60432 Gauntlets of the Elite Guard display 7698
  known '?' item: 60435 Sabatons of the Elite Guard display 7255
  known '?' item: 33278 Maw of Gluttony display 69050

## D. NPC weapons with no drawable stand-in (shown empty-handed): 44 items, 410 NPC spawns affected
item 61240 Greymane Shield - Monster | display 66376 | 4 npcs, 82 spawns
item 36010 kazgrim test totem a | display 61042 | 2 npcs, 40 spawns
item 80223 Farstrider Lodge Protector's Bow | display 40143 | 5 npcs, 35 spawns
item 80222 Farstrider Lodge Protector's Sword | display 35898 | 3 npcs, 33 spawns
item 26 Thalassian Staff | display 40047 | 7 npcs, 29 spawns
item 4130 Smotts' Compass | display 6562 | 4 npcs, 28 spawns
item 41721 Bulwark of the Old Horde | display 68460 | 1 npcs, 27 spawns
item 61669 Flameforged Handaxe | display 8455 | 1 npcs, 21 spawns
item 61241 Gilneas Rebel Shield - Monster | display 66377 | 1 npcs, 17 spawns
item 80542 Quel'dorei Cleric's Staff | display 60688 | 2 npcs, 13 spawns
item 18661 Schematic: World Enlarger | display 15274 | 1 npcs, 12 spawns
item 60787 Scythe of the Harvest | display 61041 | 1 npcs, 11 spawns
item 80534 Quel'dorei Guardian's Battle Glaive | display 40066 | 1 npcs, 10 spawns
item 80546 Quel'dorei Ranger's Longbow | display 40143 | 1 npcs, 10 spawns
item 60752 Sentinel's Glaive | display 4289 | 2 npcs, 9 spawns
item 81060 Tempered Runeblade | display 60983 | 3 npcs, 5 spawns
item 4294 Pattern: Hillman's Belt | display 1102 | 1 npcs, 4 spawns
item 61330 Dagger of Sinister Secrets | display 66383 | 2 npcs, 3 spawns
item 80543 Quel'dorei Defender's Bulwark | display 40068 | 2 npcs, 2 spawns
item 61751 "Monster - Item, Sword - Kheyna" | display 40027 | 2 npcs, 2 spawns
item 36012 kazgrim test totem c | display 61044 | 1 npcs, 2 spawns
item 80876 Silvermoon Bow | display 40053 | 1 npcs, 1 spawns
item 81276 "Monster - Item, Sword - Kheyna" | display 50189 | 2 npcs, 1 spawns
item 83093 Monster - Staff2H, Outerend | display 66120 | 1 npcs, 1 spawns
item 5085 Bristleback Quilboar Tusk | display 7209 | 1 npcs, 1 spawns
item 7873 Dragon-Shaped Cookie | display 21632 | 1 npcs, 1 spawns
item 61308 Gavel of Gilnean Justice | display 66389 | 1 npcs, 1 spawns
item 61307 Worgen Hunter Musket | display 66395 | 1 npcs, 1 spawns
item 61328 Wolfblood | display 60979 | 1 npcs, 1 spawns
item 81199 Ancestral War Totem | display 5415 | 1 npcs, 1 spawns
