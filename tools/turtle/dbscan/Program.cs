// Offline scan: apply tortoise-wow world SQL (base + dated updates) for the tables that carry models,
// then check every model reference against the client lists the proxy uses.
using System.Text;
using System.Text.RegularExpressions;

string sqlDir = args[0];      // ...\tortoise-wow\sql
string csvDir = args[1];      // ...\HermesProxy-Stock\CSV
string logDir = args[2];      // ...\HermesProxy-Stock (stock-*.txt logs)
string outDir = args[3];
string nearName =args.Length > 4 ? args[4] : null;   // optional: list everything spawned near this creature

var keep = new Dictionary<string, (string Key, string[] Cols)>
{
    ["creature_template"] = ("entry", new[] { "entry", "display_id1", "display_id2", "display_id3", "display_id4", "mount_display_id", "name", "equipment_id" }),
    ["gameobject_template"] = ("entry", new[] { "entry", "type", "displayId", "name" }),
    ["item_template"] = ("entry", new[] { "entry", "class", "subclass", "name", "display_id", "inventory_type" }),
    ["creature_equip_template"] = ("entry", new[] { "entry", "equipentry1", "equipentry2", "equipentry3" }),
    ["creature"] = ("guid", new[] { "guid", "id", "id2", "id3", "id4", "map", "position_x", "position_y", "position_z" }),
    ["gameobject"] = ("guid", new[] { "guid", "id", "map", "position_x", "position_y", "position_z" }),
};
var tables = keep.Keys.ToDictionary(k => k, k => new Dictionary<long, Dictionary<string, string>>());
var columns = new Dictionary<string, List<string>>();
var stats = new Dictionary<string, int>();
var unhandled = new List<string>();
void Count(string k) => stats[k] = stats.TryGetValue(k, out var v) ? v + 1 : 1;

var baseFiles = Directory.GetFiles(Path.Combine(sqlDir, "base"), "*.sql").OrderBy(f => f).ToList();
var updFiles = Directory.GetFiles(Path.Combine(sqlDir, "database_updates", "world"), "*.sql").OrderBy(f => Path.GetFileName(f)).ToList();
foreach (var f in baseFiles.Concat(updFiles))
{
    string name = Path.GetFileName(f);
    if (!name.StartsWith("20") && !keep.Keys.Any(t => name == $"tw_world_{t}.sql")) continue;
    foreach (var st in Statements(File.ReadAllText(f)))
        Apply(st, name);
}

// ---------- client lists ----------
HashSet<long> Col(string file, int col, bool header = true)
{
    var set = new HashSet<long>();
    var path = Path.Combine(csvDir, file);
    if (!File.Exists(path)) return set;
    foreach (var line in File.ReadLines(path).Skip(header ? 1 : 0))
    {
        var p = line.Split(',');
        if (p.Length > col && long.TryParse(p[col].Trim('"', ' ', '\r'), out var v)) set.Add(v);
    }
    return set;
}
var creatureKnown = Col("CreatureDisplayInfo.csv", 0); creatureKnown.UnionWith(Col(@"Hotfix\CreatureDisplayInfo1.csv", 0));
var wmo = Col("GameObjectWmoDisplays1.csv", 0);
var itemModelKnown = Col("ItemAppearance1.csv", 2); itemModelKnown.UnionWith(Col(@"Hotfix\ItemDisplayInfo1.csv", 0));
var itemIconKnown = Col("ItemDisplayIdToFileDataId1.csv", 0);
var clientApp = Col("ClientItemAppearances1.csv", 0);
var imaItems = Col("ItemModifiedAppearance1.csv", 1);
var item1Inv = new Dictionary<long, int>();
foreach (var line in File.ReadLines(Path.Combine(csvDir, "Item1.csv")).Skip(1)) { var p = line.Split(','); if (long.TryParse(p[0], out var id) && int.TryParse(p[4], out var inv)) item1Inv[id] = inv; }
var gearByDisplay = new HashSet<long>();
foreach (var line in File.ReadLines(Path.Combine(csvDir, "ItemIdToDisplayId1.csv")).Skip(1))
{
    var p = line.Split(','); if (p.Length < 2) continue;
    if (long.TryParse(p[0], out var id) && long.TryParse(p[1], out var d) && item1Inv.TryGetValue(id, out var inv) &&
        new[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 25, 26 }.Contains(inv)) gearByDisplay.Add(d);
}
// displays seen in our proxy logs (rendered without a crash unless listed as crashers)
var seenUnit = new HashSet<long>(); var seenGo = new HashSet<long>();
foreach (var f in Directory.GetFiles(logDir, "stock-*.txt"))
    foreach (Match m in Regex.Matches(new StreamReader(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)).ReadToEnd(), @"\[CreateTrace\] (Unit|GameObject) entry=\d+ displayId=(\d+)"))
        (m.Groups[1].Value == "Unit" ? seenUnit : seenGo).Add(long.Parse(m.Groups[2].Value));

long L(Dictionary<string, string> r, string c) => r.TryGetValue(c, out var v) && long.TryParse(v, out var x) ? x : 0;
string S(Dictionary<string, string> r, string c) => r.TryGetValue(c, out var v) ? v : "";

// spawn counts
var cSpawns = new Dictionary<long, int>(); var cMaps = new Dictionary<long, HashSet<long>>();
foreach (var r in tables["creature"].Values)
    foreach (var c in new[] { "id", "id2", "id3", "id4" }) { var id = L(r, c); if (id == 0) continue; cSpawns[id] = cSpawns.GetValueOrDefault(id) + 1; (cMaps.TryGetValue(id, out var ms) ? ms : cMaps[id] = new()).Add(L(r, "map")); }
var gSpawns = new Dictionary<long, int>();
foreach (var r in tables["gameobject"].Values) { var id = L(r, "id"); gSpawns[id] = gSpawns.GetValueOrDefault(id) + 1; }

if (nearName != null)
{
    double F(Dictionary<string, string> r, string c) => double.TryParse(S(r, c), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
    var target = tables["creature_template"].Values.Where(t => S(t, "name") == nearName).Select(t => L(t, "entry")).ToHashSet();
    foreach (var a in tables["creature"].Values.Where(r => target.Contains(L(r, "id"))))
    {
        Console.WriteLine($"== {nearName} spawn {L(a, "guid")} map {L(a, "map")} at {F(a, "position_x"):0.0} {F(a, "position_y"):0.0} {F(a, "position_z"):0.0}");
        double Dist(Dictionary<string, string> r) => L(r, "map") != L(a, "map") ? double.MaxValue :
            Math.Sqrt(Math.Pow(F(r, "position_x") - F(a, "position_x"), 2) + Math.Pow(F(r, "position_y") - F(a, "position_y"), 2) + Math.Pow(F(r, "position_z") - F(a, "position_z"), 2));
        foreach (var r in tables["gameobject"].Values.Where(r => Dist(r) < 25).OrderBy(Dist))
        {
            var t = tables["gameobject_template"].GetValueOrDefault(L(r, "id")) ?? new();
            Console.WriteLine($"  GO   {Dist(r),5:0.0}y entry {L(r, "id")} display {L(t, "displayId")} type {L(t, "type")} {S(t, "name")}");
        }
        foreach (var r in tables["creature"].Values.Where(r => Dist(r) < 25).OrderBy(Dist))
        {
            var t = tables["creature_template"].GetValueOrDefault(L(r, "id")) ?? new();
            Console.WriteLine($"  NPC  {Dist(r),5:0.0}y entry {L(r, "id")} display {L(t, "display_id1")} {S(t, "name")}{(creatureKnown.Contains(L(t, "display_id1")) ? "" : " [display not in client list]")}");
        }
    }
    return;
}

var sb = new StringBuilder();
sb.AppendLine("# Tortoise-WoW world data vs. client model lists");
sb.AppendLine($"base files: {baseFiles.Count}, update files: {updFiles.Count}");
foreach (var t in keep.Keys) sb.AppendLine($"  {t}: {tables[t].Count} rows");
sb.AppendLine("statements: " + string.Join(", ", stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
sb.AppendLine($"unhandled statements touching model columns: {unhandled.Count}");
foreach (var u in unhandled.Take(15)) sb.AppendLine("  " + u);

// ---------- A. NPC models ----------
var npcBad = new Dictionary<long, List<(long Entry, string Name, bool Mount)>>();
foreach (var r in tables["creature_template"].Values)
{
    foreach (var c in new[] { "display_id1", "display_id2", "display_id3", "display_id4", "mount_display_id" })
    {
        var d = L(r, c); if (d <= 0 || creatureKnown.Contains(d)) continue;
        (npcBad.TryGetValue(d, out var l) ? l : npcBad[d] = new()).Add((L(r, "entry"), S(r, "name"), c == "mount_display_id"));
    }
}
var npcRows = npcBad.Select(kv => new { Display = kv.Key, Npcs = kv.Value, Spawns = kv.Value.Sum(n => cSpawns.GetValueOrDefault(n.Entry)) })
                    .OrderByDescending(x => x.Spawns).ToList();
sb.AppendLine();
sb.AppendLine($"## A. NPC models missing from the client list: {npcRows.Count} displays, {npcRows.Count(x => x.Spawns > 0)} of them spawned in the world ({npcRows.Sum(x => x.Spawns)} spawns)");
sb.AppendLine("display | spawns | npcs (entry name)");
foreach (var x in npcRows.Where(x => x.Spawns > 0).Take(60))
    sb.AppendLine($"{x.Display} | {x.Spawns} | {string.Join("; ", x.Npcs.Take(3).Select(n => $"{n.Entry} {n.Name}{(n.Mount ? " [mount]" : "")}"))}{(x.Npcs.Count > 3 ? $" (+{x.Npcs.Count - 3})" : "")}");

// ---------- B. GameObjects ----------
int goDropped = 0, goWmo = 0, goTransport = 0, goOther = 0;
var goCustom = new Dictionary<long, List<(long Entry, string Name, long Type)>>();
foreach (var r in tables["gameobject_template"].Values)
{
    long e = L(r, "entry"), d = L(r, "displayId"), type = L(r, "type");
    if (e >= 1000000) { goDropped++; continue; }
    if (type == 11 || type == 15) goTransport++;
    if (wmo.Contains(d)) { goWmo++; continue; }
    goOther++;
    if (e > 211000 && d > 0)
        (goCustom.TryGetValue(d, out var l) ? l : goCustom[d] = new()).Add((e, S(r, "name"), type));
}
var goRows = goCustom.Select(kv => new { Display = kv.Key, Gos = kv.Value, Spawns = kv.Value.Sum(g => gSpawns.GetValueOrDefault(g.Entry)), Seen = seenGo.Contains(kv.Key) })
                     .Where(x => x.Spawns > 0).OrderByDescending(x => x.Spawns).ToList();
sb.AppendLine();
sb.AppendLine($"## B. GameObjects: {goDropped} templates dropped by the proxy (entry >= 1000000), {goWmo} use a WMO model (flag now applied), {goTransport} transports.");
sb.AppendLine($"Custom-range objects (entry 211001-999999, non-WMO, spawned) the proxy still sends: {goRows.Count} displays, {goRows.Sum(x => x.Spawns)} spawns. No client GO model list exists, so these are unverified; 'seen' = rendered in our logs.");
sb.AppendLine("display | spawns | seen | objects (entry name type)");
foreach (var x in goRows.Take(60))
    sb.AppendLine($"{x.Display} | {x.Spawns} | {(x.Seen ? "seen" : "-")} | {string.Join("; ", x.Gos.Take(3).Select(g => $"{g.Entry} {g.Name} t{g.Type}"))}{(x.Gos.Count > 3 ? $" (+{x.Gos.Count - 3})" : "")}");

// ---------- C. Items ----------
var noModel = new List<(long Entry, string Name, long Display, long Inv)>(); var noIcon = new List<(long Entry, string Name, long Display, bool Known)>();
foreach (var r in tables["item_template"].Values)
{
    long e = L(r, "entry"), d = L(r, "display_id"), inv = L(r, "inventory_type"), cls = L(r, "class");
    bool known = item1Inv.ContainsKey(e) || clientApp.Contains(e);
    if (d > 0 && !itemIconKnown.Contains(d) && !item1Inv.ContainsKey(e)) noIcon.Add((e, S(r, "name"), d, known));
    // slots that draw a model on the character (no neck 2, finger 11, trinket 12, bag 18, ammo 24, relic 28)
    bool wearable = new long[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 25, 26 }.Contains(inv) && (cls == 2 || cls == 4);
    if (wearable && d > 0 && !known && !itemModelKnown.Contains(d)) noModel.Add((e, S(r, "name"), d, inv));
}
sb.AppendLine();
sb.AppendLine($"## C1. Wearable items the client doesn't know whose display has no client model: {noModel.Count} items, {noModel.Select(x => x.Display).Distinct().Count()} displays");
sb.AppendLine("(these show no model when worn; if the proxy pushed appearance records for them they would dangle)");
foreach (var g in noModel.GroupBy(x => x.Display).OrderByDescending(g => g.Count()).Take(40))
    sb.AppendLine($"{g.Key} | {g.Count()} items | {string.Join("; ", g.Take(3).Select(x => $"{x.Entry} {x.Name}"))}");
sb.AppendLine();
sb.AppendLine($"## C2. Items whose icon can't be resolved (show '?'): {noIcon.Count} items ({noIcon.Count(x => x.Known)} of them client-known custom items), {noIcon.Select(x => x.Display).Distinct().Count()} displays");
foreach (var x in noIcon.Where(x => new long[] { 60432, 60435, 60999, 61000, 33278 }.Contains(x.Entry)))
    sb.AppendLine($"  known '?' item: {x.Entry} {x.Name} display {x.Display}");

// ---------- D. NPC weapons ----------
var npcByEquip = new Dictionary<long, List<long>>();
foreach (var r in tables["creature_template"].Values) { var eq = L(r, "equipment_id"); if (eq != 0) (npcByEquip.TryGetValue(eq, out var l) ? l : npcByEquip[eq] = new()).Add(L(r, "entry")); }
var weapBad = new Dictionary<long, (string Name, long Display, int Spawns, int Npcs)>();
foreach (var r in tables["creature_equip_template"].Values)
{
    var npcs = npcByEquip.GetValueOrDefault(L(r, "entry")) ?? new List<long>();
    int spawns = npcs.Sum(n => cSpawns.GetValueOrDefault(n));
    foreach (var c in new[] { "equipentry1", "equipentry2", "equipentry3" })
    {
        var it = L(r, c); if (it == 0) continue;
        if (!tables["item_template"].TryGetValue(it, out var ir)) continue;
        var d = L(ir, "display_id");
        if (gearByDisplay.Contains(d)) continue;
        var prev = weapBad.GetValueOrDefault(it);
        weapBad[it] = (S(ir, "name"), d, prev.Spawns + spawns, prev.Npcs + npcs.Count);
    }
}
sb.AppendLine();
sb.AppendLine($"## D. NPC weapons with no drawable stand-in (shown empty-handed): {weapBad.Count} items, {weapBad.Values.Sum(w => w.Spawns)} NPC spawns affected");
foreach (var w in weapBad.OrderByDescending(w => w.Value.Spawns).Take(30))
    sb.AppendLine($"item {w.Key} {w.Value.Name} | display {w.Value.Display} | {w.Value.Npcs} npcs, {w.Value.Spawns} spawns");

Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "report.md"), sb.ToString());
File.WriteAllLines(Path.Combine(outDir, "npc_missing_displays.csv"), new[] { "display,spawns,npc_entries" }.Concat(npcRows.Select(x => $"{x.Display},{x.Spawns},{string.Join(" ", x.Npcs.Select(n => n.Entry))}")));
File.WriteAllLines(Path.Combine(outDir, "go_custom_displays.csv"), new[] { "display,spawns,seen,go_entries" }.Concat(goRows.Select(x => $"{x.Display},{x.Spawns},{x.Seen},{string.Join(" ", x.Gos.Select(g => g.Entry))}")));
File.WriteAllLines(Path.Combine(outDir, "items_no_model.csv"), new[] { "entry,display,inventory_type,name" }.Concat(noModel.Select(x => $"{x.Entry},{x.Display},{x.Inv},\"{x.Name}\"")));
File.WriteAllLines(Path.Combine(outDir, "items_no_icon.csv"), new[] { "entry,display,client_known,name" }.Concat(noIcon.Select(x => $"{x.Entry},{x.Display},{x.Known},\"{x.Name}\"")));
Console.WriteLine(sb.ToString());

// ================= SQL handling =================
void Apply(string st, string file)
{
    var m = Regex.Match(st, @"^CREATE TABLE\s+(?:IF NOT EXISTS\s+)?`?(\w+)`?\s*\((.*)\)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    if (m.Success)
    {
        var t = m.Groups[1].Value; if (!keep.ContainsKey(t)) return;
        columns[t] = Regex.Matches(m.Groups[2].Value, @"^\s*`(\w+)`", RegexOptions.Multiline).Select(x => x.Groups[1].Value).ToList();
        return;
    }
    m = Regex.Match(st, @"^(INSERT(?:\s+IGNORE)?|REPLACE)\s+INTO\s+`?(\w+)`?\s*(\(([^)]*)\))?\s*VALUES\s*(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    if (m.Success)
    {
        var t = m.Groups[2].Value; if (!keep.ContainsKey(t)) return;
        bool ignore = m.Groups[1].Value.ToUpper().Contains("IGNORE");
        var cols = m.Groups[3].Success ? m.Groups[4].Value.Split(',').Select(c => c.Trim().Trim('`')).ToList() : columns.GetValueOrDefault(t);
        if (cols == null) { unhandled.Add($"{file}: insert into {t} before its CREATE TABLE"); return; }
        var (key, kept) = keep[t];
        foreach (var row in Tuples(m.Groups[5].Value))
        {
            if (row.Count != cols.Count) { Count("bad_tuple"); continue; }
            var rec = new Dictionary<string, string>();
            for (int i = 0; i < cols.Count; i++) if (kept.Contains(cols[i])) rec[cols[i]] = row[i];
            if (!rec.TryGetValue(key, out var kv) || !long.TryParse(kv, out var k)) { Count("insert_no_key"); continue; }
            if (ignore && tables[t].ContainsKey(k)) continue;
            if (tables[t].TryGetValue(k, out var old) && m.Groups[3].Success) { foreach (var x in rec) old[x.Key] = x.Value; }
            else tables[t][k] = rec;
            Count($"{t}.insert");
        }
        return;
    }
    m = Regex.Match(st, @"^UPDATE\s+`?(\w+)`?\s+SET\s+(.*?)\s+WHERE\s+(.*?)(\s+LIMIT\s+\d+)?$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    if (m.Success)
    {
        var t = m.Groups[1].Value; if (!keep.ContainsKey(t)) return;
        var (key, kept) = keep[t];
        var sets = new Dictionary<string, string>(); bool expr = false;
        foreach (var a in SplitTop(m.Groups[2].Value))
        {
            var am = Regex.Match(a.Trim(), @"^`?(\w+)`?\s*=\s*(.+)$", RegexOptions.Singleline);
            if (!am.Success) { expr = true; continue; }
            var col = am.Groups[1].Value; if (!kept.Contains(col)) continue;
            var rhs = am.Groups[2].Value.Trim();
            var lit = Literal(rhs);
            if (Regex.IsMatch(rhs, $@"^`?{key}`?$")) sets[col] = "\u0001key"; // e.g. equipment_id = entry
            else if (lit == null) expr = true; else sets[col] = lit;
        }
        bool touches = kept.Any(c => Regex.IsMatch(m.Groups[2].Value, $@"`?\b{c}\b`?\s*=", RegexOptions.IgnoreCase));
        if (!touches) return;
        var keys = Keys(m.Groups[3].Value, key);
        if (keys == null || expr) { unhandled.Add($"{file}: {Short(st)}"); return; }
        foreach (var k in keys) if (tables[t].TryGetValue(k, out var rec)) foreach (var s in sets) rec[s.Key] = s.Value == "\u0001key" ? k.ToString() : s.Value;
        Count($"{t}.update");
        return;
    }
    m = Regex.Match(st, @"^DELETE\s+FROM\s+`?(\w+)`?\s+WHERE\s+(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    if (m.Success)
    {
        var t = m.Groups[1].Value; if (!keep.ContainsKey(t)) return;
        var keys = Keys(m.Groups[2].Value, keep[t].Key);
        if (keys == null) { if (t.EndsWith("template")) unhandled.Add($"{file}: {Short(st)}"); return; }
        foreach (var k in keys) tables[t].Remove(k);
        Count($"{t}.delete");
    }
}

string Short(string s) { s = Regex.Replace(s, @"\s+", " "); return s.Length > 150 ? s[..150] + "…" : s; }

List<long> Keys(string cond, string key)
{
    var c = Regex.Replace(cond.Replace("`", ""), @"\s+", " ").Trim().TrimEnd(';');
    var m = Regex.Match(c, $@"^(?:\w+\.)?{key}\s*=\s*'?(\d+)'?$", RegexOptions.IgnoreCase);
    if (m.Success) return new List<long> { long.Parse(m.Groups[1].Value) };
    m = Regex.Match(c, $@"^(?:\w+\.)?{key}\s+IN\s*\(([\d\s,']+)\)$", RegexOptions.IgnoreCase);
    if (m.Success) return m.Groups[1].Value.Split(',').Select(x => long.Parse(x.Trim().Trim('\''))).ToList();
    m = Regex.Match(c, $@"^(?:\w+\.)?{key}\s+BETWEEN\s+(\d+)\s+AND\s+(\d+)$", RegexOptions.IgnoreCase);
    if (m.Success) { long a = long.Parse(m.Groups[1].Value), b = long.Parse(m.Groups[2].Value); var l = new List<long>(); for (var i = a; i <= b && l.Count < 200000; i++) l.Add(i); return l; }
    return null;
}

string Literal(string v)
{
    if (v.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return "";
    if (Regex.IsMatch(v, @"^-?\d+(\.\d+)?([eE][-+]?\d+)?$")) return v;
    if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'') return Unquote(v);
    return null;
}

string Unquote(string v) => v[1..^1].Replace("\\'", "'").Replace("''", "'").Replace("\\\\", "\\");

IEnumerable<string> SplitTop(string s)
{
    var sb2 = new StringBuilder(); bool q = false; int depth = 0;
    for (int i = 0; i < s.Length; i++)
    {
        char ch = s[i];
        if (q) { sb2.Append(ch); if (ch == '\\' && i + 1 < s.Length) { sb2.Append(s[++i]); } else if (ch == '\'') q = false; continue; }
        if (ch == '\'') q = true;
        else if (ch == '(') depth++;
        else if (ch == ')') depth--;
        else if (ch == ',' && depth == 0) { yield return sb2.ToString(); sb2.Clear(); continue; }
        sb2.Append(ch);
    }
    if (sb2.Length > 0) yield return sb2.ToString();
}

IEnumerable<List<string>> Tuples(string s)
{
    int i = 0;
    while (i < s.Length)
    {
        while (i < s.Length && s[i] != '(') i++;
        if (i >= s.Length) yield break;
        i++;
        var vals = new List<string>(); var cur = new StringBuilder(); bool quoted = false, wasQuoted = false;
        while (i < s.Length)
        {
            char ch = s[i];
            if (quoted)
            {
                if (ch == '\\' && i + 1 < s.Length) { cur.Append(s[i + 1]); i += 2; continue; }
                if (ch == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { cur.Append('\''); i += 2; continue; }
                if (ch == '\'') { quoted = false; i++; continue; }
                cur.Append(ch); i++; continue;
            }
            if (ch == '\'') { quoted = true; wasQuoted = true; i++; continue; }
            if (ch == ',' || ch == ')')
            {
                var v = cur.ToString().Trim();
                vals.Add(!wasQuoted && v.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : v);
                cur.Clear(); wasQuoted = false; i++;
                if (ch == ')') break;
                continue;
            }
            cur.Append(ch); i++;
        }
        yield return vals;
    }
}

IEnumerable<string> Statements(string text)
{
    var sb2 = new StringBuilder(); bool q = false; int n = text.Length;
    for (int i = 0; i < n; i++)
    {
        char ch = text[i];
        if (q)
        {
            sb2.Append(ch);
            if (ch == '\\' && i + 1 < n) sb2.Append(text[++i]);
            else if (ch == '\'') q = false;
            continue;
        }
        if (ch == '-' && i + 1 < n && text[i + 1] == '-' && (i + 2 >= n || char.IsWhiteSpace(text[i + 2]))) { while (i < n && text[i] != '\n') i++; sb2.Append('\n'); continue; }
        if (ch == '#') { while (i < n && text[i] != '\n') i++; sb2.Append('\n'); continue; }
        if (ch == '/' && i + 1 < n && text[i + 1] == '*') { int end = text.IndexOf("*/", i + 2); i = end < 0 ? n : end + 1; continue; }
        if (ch == '\'') { q = true; sb2.Append(ch); continue; }
        if (ch == ';') { var st = sb2.ToString().Trim(); if (st.Length > 0) yield return st; sb2.Clear(); continue; }
        sb2.Append(ch);
    }
    var last = sb2.ToString().Trim(); if (last.Length > 0) yield return last;
}
