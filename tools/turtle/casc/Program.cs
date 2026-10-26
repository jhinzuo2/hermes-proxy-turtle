// Reads tables out of the 1.14.2 client's CASC storage (incl. the Ravencraft launcher's loose cdn files).
//   casc <client dir> table <fdid> <out.csv>     DB2 -> CSV (integer columns), prints table/layout hash
//   casc <client dir> file <fdid> <out file>     raw file
//   casc <client dir> ids                        every file id the client has
//   casc <client dir> loose                      file ids whose data comes from the launcher's cdn folder
//   casc dbc <file.dbc> <out.csv>                 1.12 WDBC -> CSV (strings decoded where a column points into the string block)
if (args.Length < 2) { Console.Error.WriteLine("usage: casc <client dir> table|file|loose ... | casc dbc <file.dbc> <out.csv>"); return 1; }
if (args[0] == "dbc")
{
    var b = File.ReadAllBytes(args[1]);
    int n = BitConverter.ToInt32(b, 4), fields = BitConverter.ToInt32(b, 8), size = BitConverter.ToInt32(b, 12), strSize = BitConverter.ToInt32(b, 16);
    int strStart = 20 + n * size;
    using var w = new StreamWriter(args[2]);
    w.WriteLine(string.Join(",", Enumerable.Range(0, fields).Select(f => $"f{f}")));
    for (int r = 0; r < n; r++)
        w.WriteLine(string.Join(",", Enumerable.Range(0, fields).Select(f =>
        {
            uint v = BitConverter.ToUInt32(b, 20 + r * size + f * 4);
            if (f == 0 || v == 0 || v >= strSize || b[strStart + v - 1] != 0) return v.ToString();
            int e = Array.IndexOf(b, (byte)0, strStart + (int)v);
            return "\"" + System.Text.Encoding.UTF8.GetString(b, strStart + (int)v, e - strStart - (int)v) + "\"";
        })));
    return 0;
}
var casc = new Casc(args[0]);
switch (args[1])
{
    case "table":
    {
        var data = casc.ReadFile(uint.Parse(args[2])) ?? throw new FileNotFoundException(args[2]);
        var t = new Wdc3(data);
        Console.WriteLine($"{args[2]}: table 0x{t.TableHash:X8} layout 0x{t.LayoutHash:X8}, {t.Rows.Count} rows, {t.Columns.Count} columns, loose={casc.IsLoose(uint.Parse(args[2]))}");
        using var w = new StreamWriter(args[3]);
        w.WriteLine(string.Join(",", t.Columns));
        foreach (var r in t.Rows.OrderBy(r => r[0]))
            w.WriteLine(t.Texts.TryGetValue(r, out var text)
                ? string.Join(",", r.Select((v, i) => text[i] != null ? "\"" + text[i].Replace("\"", "\"\"") + "\"" : v.ToString()))
                : string.Join(",", r));
        break;
    }
    case "file":
        File.WriteAllBytes(args[3], casc.ReadFile(uint.Parse(args[2])) ?? throw new FileNotFoundException(args[2]));
        break;
    case "ids":
        foreach (var id in casc.FileIds.OrderBy(i => i)) Console.WriteLine(id);
        break;
    case "loose":
        foreach (var id in casc.FileIds.Where(casc.IsLoose).OrderBy(i => i)) Console.WriteLine(id);
        break;
}
return 0;
