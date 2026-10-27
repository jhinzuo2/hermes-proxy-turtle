// Minimal WDC3 (DB2) reader: integer columns only (string columns come out as their raw offsets).
// Enough for the id/display/icon tables the proxy cares about.
class Wdc3
{
    public uint TableHash, LayoutHash;
    public List<string> Columns = new() { "ID" };
    public List<long[]> Rows = new();   // [0] = id
    public Dictionary<long[], string[]> Texts = new();   // 4-byte columns that point into the string table

    public Wdc3(byte[] b, ISet<int> sparseStrings = null)
    {
        if (BitConverter.ToUInt32(b, 0) != 0x33434457) throw new InvalidDataException("not WDC3");
        int recordCount = I(b, 4), fieldCount = I(b, 8), recordSize = I(b, 12);
        TableHash = (uint)I(b, 20); LayoutHash = (uint)I(b, 24);
        int flags = BitConverter.ToUInt16(b, 40), idIndex = BitConverter.ToUInt16(b, 42);
        int totalFields = I(b, 44), storageInfoSize = I(b, 56), commonSize = I(b, 60), palletSize = I(b, 64), sectionCount = I(b, 68);
        int pos = 72;
        var sections = new List<int[]>();
        for (int s = 0; s < sectionCount; s++, pos += 40)
            sections.Add(new[] { I(b, pos + 8), I(b, pos + 12), I(b, pos + 16), I(b, pos + 20), I(b, pos + 24), I(b, pos + 28), I(b, pos + 32), I(b, pos + 36) });
        var fieldSize = new int[fieldCount];
        for (int f = 0; f < fieldCount; f++, pos += 4) fieldSize[f] = BitConverter.ToInt16(b, pos);
        var info = new List<(int offBits, int sizeBits, int addSize, int type, int v1, int v2, int v3)>();
        for (int f = 0; f < totalFields; f++, pos += 24)
            info.Add((BitConverter.ToUInt16(b, pos), BitConverter.ToUInt16(b, pos + 2), I(b, pos + 4), I(b, pos + 8), I(b, pos + 12), I(b, pos + 16), I(b, pos + 20)));
        int palletStart = pos, commonStart = pos + palletSize;
        var palletOff = new int[totalFields]; var common = new Dictionary<int, int>[totalFields];
        int pp = palletStart, cp = commonStart;
        for (int f = 0; f < totalFields; f++)
        {
            var fi = info[f];
            if (fi.type == 3 || fi.type == 4) { palletOff[f] = pp; pp += fi.addSize; }
            if (fi.type == 2)
            {
                common[f] = new();
                for (int e = cp; e < cp + fi.addSize; e += 8) common[f][I(b, e)] = I(b, e + 4);
                cp += fi.addSize;
            }
        }
        var arrayLen = new int[totalFields];
        for (int f = 0; f < totalFields; f++)
        {
            var fi = info[f];
            int elemBits = f < fieldCount ? 32 - fieldSize[f] : 32;
            arrayLen[f] = fi.type == 4 ? fi.v3 : fi.type == 0 && elemBits > 0 ? Math.Max(1, fi.sizeBits / elemBits) : 1;
            for (int a = 0; a < arrayLen[f]; a++) Columns.Add(arrayLen[f] > 1 ? $"f{f}_{a}" : $"f{f}");
        }
        if ((flags & 1) != 0)
        {
            // Sparse table (offset map, e.g. ItemSparse): variable-length records with inline strings. Which fields
            // are strings is not in the header, so the caller names them.
            if (sparseStrings == null) throw new NotSupportedException("sparse table: pass its inline string fields (strings=...)");
            foreach (var s in sections)
            {
                int recordsEnd = s[3], idListSize = s[4], relSize = s[5], mapCount = s[6], copyCount = s[7];
                int offsetMap = recordsEnd + idListSize + copyCount * 8, mapIds = offsetMap + mapCount * 6 + relSize;
                for (int r = 0; r < mapCount; r++)
                {
                    int at = I(b, offsetMap + r * 6);
                    var row = new long[Columns.Count];
                    var text = new string[Columns.Count];
                    row[0] = (uint)I(b, mapIds + r * 4);
                    int c = 1;
                    for (int f = 0; f < totalFields; f++)
                        for (int a = 0; a < arrayLen[f]; a++, c++)
                        {
                            if (sparseStrings.Contains(f)) { text[c] = CStr(b, at); at = Array.IndexOf(b, (byte)0, at) + 1; continue; }
                            int bytes = (32 - fieldSize[f]) / 8;
                            row[c] = bytes == 1 ? b[at] : bytes == 2 ? BitConverter.ToUInt16(b, at) : bytes == 8 ? BitConverter.ToInt64(b, at) : (uint)I(b, at);
                            at += bytes;
                        }
                    Rows.Add(row);
                    Texts[row] = text;
                }
            }
            return;
        }
        if (sections.Any(s => s[5] > 0)) Columns.Add("rel");   // relationship map (foreign key, e.g. TransportAnimation.TransportID)

        foreach (var s in sections)
        {
            int fileOffset = s[0], count = s[1], strSize = s[2], idListSize = s[4], relSize = s[5], copyCount = s[7];
            int recStart = fileOffset;
            int strStart = recStart + count * recordSize;
            int idList = recStart + count * recordSize + strSize;
            int copyStart = idList + idListSize;
            var sectionRows = new List<long[]>();
            for (int r = 0; r < count; r++)
            {
                int rec = recStart + r * recordSize;
                var row = new long[Columns.Count];
                var text = new string[Columns.Count];
                int c = 1;
                for (int f = 0; f < totalFields; f++)
                {
                    var fi = info[f];
                    for (int a = 0; a < arrayLen[f]; a++, c++)
                    {
                        switch (fi.type)
                        {
                            case 0:
                                int bytes = (32 - fieldSize[f]) / 8;
                                int at = rec + fi.offBits / 8 + a * bytes;
                                row[c] = bytes == 1 ? b[at] : bytes == 2 ? BitConverter.ToUInt16(b, at) : bytes == 8 ? BitConverter.ToInt64(b, at) : (uint)I(b, at);
                                if (bytes == 4 && at + row[c] >= strStart && at + row[c] < strStart + strSize) text[c] = CStr(b, (int)(at + row[c]));
                                break;
                            case 1: case 5:
                                long v = Bits(b, rec, fi.offBits, fi.sizeBits);
                                if (fi.type == 5 && ((v >> (fi.sizeBits - 1)) & 1) != 0) v -= 1L << fi.sizeBits;
                                row[c] = v; break;
                            case 2: row[c] = fi.v1; break;   // filled in below once the id is known
                            case 3: row[c] = (uint)I(b, palletOff[f] + (int)Bits(b, rec, fi.offBits, fi.sizeBits) * 4); break;
                            case 4: row[c] = (uint)I(b, palletOff[f] + ((int)Bits(b, rec, fi.offBits, fi.sizeBits) * arrayLen[f] + a) * 4); break;
                        }
                    }
                }
                row[0] = idListSize > 0 ? (uint)I(b, idList + r * 4) : row[1 + ColumnOf(idIndex, arrayLen)];
                c = 1;
                for (int f = 0; f < totalFields; f++, c += arrayLen[f - 1])
                    if (info[f].type == 2 && common[f].TryGetValue((int)row[0], out int cv)) row[c] = cv;
                sectionRows.Add(row);
                if (text.Any(t => t != null)) Texts[row] = text;
            }
            if (relSize > 0)
            {
                int relStart = copyStart + copyCount * 8, relCount = I(b, relStart);
                for (int k = 0; k < relCount; k++)
                {
                    int foreign = I(b, relStart + 12 + k * 8), recIndex = I(b, relStart + 16 + k * 8);
                    if (recIndex < count) sectionRows[recIndex][Columns.Count - 1] = (uint)foreign;
                }
            }
            var byId = sectionRows.GroupBy(r => r[0]).ToDictionary(g => g.Key, g => g.First());
            for (int k = 0; k < copyCount; k++)
            {
                int newId = I(b, copyStart + k * 8), src = I(b, copyStart + k * 8 + 4);
                if (byId.TryGetValue(src, out var srcRow)) { var cp2 = (long[])srcRow.Clone(); cp2[0] = newId; sectionRows.Add(cp2); }
            }
            Rows.AddRange(sectionRows);
        }
    }

    static string CStr(byte[] b, int p) { int e = Array.IndexOf(b, (byte)0, p); return System.Text.Encoding.UTF8.GetString(b, p, e - p); }

    static int ColumnOf(int field, int[] arrayLen) { int c = 0; for (int f = 0; f < field; f++) c += arrayLen[f]; return c; }

    static long Bits(byte[] b, int rec, int offBits, int sizeBits)
    {
        int start = rec + offBits / 8;
        ulong v = 0;
        for (int i = 0; i < 8 && start + i < b.Length; i++) v |= (ulong)b[start + i] << (8 * i);
        v >>= offBits & 7;
        return (long)(sizeBits >= 64 ? v : v & ((1UL << sizeBits) - 1));
    }

    static int I(byte[] b, int p) => BitConverter.ToInt32(b, p);
}
