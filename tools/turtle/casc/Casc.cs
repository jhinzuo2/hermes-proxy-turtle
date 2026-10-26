using System.IO.Compression;

// Minimal read-only CASC reader for a local WoW client install (the Ravencraft 1.14.2 package).
// Lookup chain: FileDataID -(root)-> CKey -(encoding)-> EKey -> bytes, where bytes come from
// a loose file the launcher keeps under cdn/.../data/xx/yy/<ekey> or from the local Data/data archives.
class Casc
{
    readonly string _dataDir;      // <client>/Data/data
    readonly string _cdnDir;       // <client>/cdn/<product>/tpr/wow
    readonly Dictionary<string, (int archive, long offset, int size)> _local = new();
    readonly Dictionary<string, string> _cToE = new();               // hex ckey -> hex ekey
    readonly Dictionary<uint, List<(uint locale, uint content, string ckey)>> _root = new();

    public Casc(string clientDir)
    {
        _dataDir = Path.Combine(clientDir, "Data", "data");
        var info = File.ReadAllLines(Path.Combine(clientDir, ".build.info"));
        var head = info[0].Split('|').Select(h => h.Split('!')[0]).ToList();
        var row = info.Skip(1).Select(l => l.Split('|')).First(r => r[head.IndexOf("Active")] == "1");
        string buildKey = row[head.IndexOf("Build Key")];
        string product = row[head.IndexOf("Product")];
        _cdnDir = Path.Combine(clientDir, "cdn", product, "tpr", "wow");

        LoadLocalIndices();

        var build = ReadConfig(buildKey);
        string encodingEKey = build["encoding"].Split(' ')[1];
        LoadEncoding(ReadByEKey(encodingEKey));
        LoadRoot(ReadByCKey(build["root"]));
        Console.Error.WriteLine($"build {buildKey}: {_local.Count} local keys, {_cToE.Count} encoding keys, {_root.Count} file ids");
    }

    Dictionary<string, string> ReadConfig(string key)
    {
        string rel = Path.Combine(key[..2], key.Substring(2, 2), key);
        string path = new[] { Path.Combine(_cdnDir, "config", rel), Path.Combine(_dataDir, "..", "config", rel) }.First(File.Exists);
        return File.ReadAllLines(path).Where(l => l.Contains(" = "))
            .ToDictionary(l => l[..l.IndexOf(" = ")], l => l[(l.IndexOf(" = ") + 3)..]);
    }

    void LoadLocalIndices()
    {
        // NN VVVVVVVV.idx: bucket NN, version VVVVVVVV; only the newest version of each bucket is live.
        var newest = Directory.GetFiles(_dataDir, "*.idx")
            .GroupBy(f => Path.GetFileName(f)[..2])
            .Select(g => g.OrderBy(f => Path.GetFileName(f)).Last());
        foreach (var file in newest)
        {
            var b = File.ReadAllBytes(file);
            int headerSize = BitConverter.ToInt32(b, 0);
            int pos = (8 + headerSize + 15) & ~15;
            int entriesSize = BitConverter.ToInt32(b, pos);
            pos += 8;
            for (int end = pos + entriesSize; pos + 18 <= end; pos += 18)
            {
                string key = Convert.ToHexString(b, pos, 9).ToLowerInvariant();
                long packed = ((long)b[pos + 9] << 32) | ((long)b[pos + 10] << 24) | ((long)b[pos + 11] << 16) | ((long)b[pos + 12] << 8) | b[pos + 13];
                int size = BitConverter.ToInt32(b, pos + 14);
                _local.TryAdd(key, ((int)(packed >> 30), packed & 0x3FFFFFFF, size));
            }
        }
    }

    public byte[] ReadByEKey(string ekey)
    {
        string loose = Path.Combine(_cdnDir, "data", ekey[..2], ekey.Substring(2, 2), ekey);
        if (File.Exists(loose))
            return Blte.Decode(File.ReadAllBytes(loose));
        if (_local.TryGetValue(ekey[..18], out var e))
        {
            using var fs = File.OpenRead(Path.Combine(_dataDir, $"data.{e.archive:D3}"));
            fs.Position = e.offset + 0x1E;   // 0x1E byte local header: reversed ekey, size, flags, checksums
            var buf = new byte[e.size - 0x1E];
            fs.ReadExactly(buf);
            return Blte.Decode(buf);
        }
        return null;
    }

    public byte[] ReadByCKey(string ckey) => _cToE.TryGetValue(ckey, out var ekey) ? ReadByEKey(ekey) : null;

    // enUS (0x2) or locale-neutral entries win; Ravencraft's own records are just more entries for the id.
    public string CKeyOf(uint fdid)
    {
        if (!_root.TryGetValue(fdid, out var list)) return null;
        var pick = list.LastOrDefault(e => (e.locale & 0x2) != 0) ;
        return pick.ckey ?? list[^1].ckey;
    }

    public byte[] ReadFile(uint fdid) => CKeyOf(fdid) is string c ? ReadByCKey(c) : null;
    public bool IsLoose(uint fdid) => CKeyOf(fdid) is string c && _cToE.TryGetValue(c, out var e)
        && File.Exists(Path.Combine(_cdnDir, "data", e[..2], e.Substring(2, 2), e));
    public IEnumerable<uint> FileIds => _root.Keys;

    void LoadEncoding(byte[] b)
    {
        int ckeySize = b[3], ekeySize = b[4];
        int cePageSize = BE16(b, 5) * 1024;
        int cePages = BE32(b, 9);
        int especSize = BE32(b, 18);
        int pos = 22 + especSize + cePages * 32;
        for (int p = 0; p < cePages; p++)
        {
            int end = pos + cePageSize;
            while (pos + 6 + ckeySize <= end && b[pos] != 0)
            {
                int keyCount = b[pos];
                string ckey = Convert.ToHexString(b, pos + 6, ckeySize).ToLowerInvariant();
                string ekey = Convert.ToHexString(b, pos + 6 + ckeySize, ekeySize).ToLowerInvariant();
                _cToE.TryAdd(ckey, ekey);
                pos += 6 + ckeySize + keyCount * ekeySize;
            }
            pos = end;
        }
    }

    void LoadRoot(byte[] b)
    {
        if (BitConverter.ToUInt32(b, 0) != 0x4D465354) { LoadLegacyRoot(b); return; }
        uint total = BitConverter.ToUInt32(b, 4), named = BitConverter.ToUInt32(b, 8);
        int pos = 12;
        if (total == 24) { total = BitConverter.ToUInt32(b, 12); named = BitConverter.ToUInt32(b, 16); pos = 24; }
        bool allowNameless = total != named;
        while (pos < b.Length)
        {
            int count = BitConverter.ToInt32(b, pos);
            uint content = BitConverter.ToUInt32(b, pos + 4), locale = BitConverter.ToUInt32(b, pos + 8);
            pos += 12;
            var ids = new uint[count];
            uint id = uint.MaxValue;
            for (int i = 0; i < count; i++, pos += 4)
                ids[i] = id = id + 1 + BitConverter.ToUInt32(b, pos);
            for (int i = 0; i < count; i++, pos += 16)
            {
                if (!_root.TryGetValue(ids[i], out var list)) _root[ids[i]] = list = new();
                list.Add((locale, content, Convert.ToHexString(b, pos, 16).ToLowerInvariant()));
            }
            if (!(allowNameless && (content & 0x10000000) != 0))
                pos += 8 * count;
        }
    }

    // Pre-MFST root: blocks of (count, content flags, locale flags, id deltas, then (ckey, name hash) pairs).
    void LoadLegacyRoot(byte[] b)
    {
        int pos = 0;
        while (pos + 12 <= b.Length)
        {
            int count = BitConverter.ToInt32(b, pos);
            uint content = BitConverter.ToUInt32(b, pos + 4), locale = BitConverter.ToUInt32(b, pos + 8);
            pos += 12;
            var ids = new uint[count];
            uint id = uint.MaxValue;
            for (int i = 0; i < count; i++, pos += 4)
                ids[i] = id = id + 1 + BitConverter.ToUInt32(b, pos);
            for (int i = 0; i < count; i++, pos += 24)
            {
                if (!_root.TryGetValue(ids[i], out var list)) _root[ids[i]] = list = new();
                list.Add((locale, content, Convert.ToHexString(b, pos, 16).ToLowerInvariant()));
            }
        }
    }

    static int BE16(byte[] b, int p) => (b[p] << 8) | b[p + 1];
    static int BE32(byte[] b, int p) => (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
}

static class Blte
{
    public static byte[] Decode(byte[] b)
    {
        if (b.Length < 8 || b[0] != 'B' || b[1] != 'L' || b[2] != 'T' || b[3] != 'E') throw new InvalidDataException("not BLTE");
        int headerSize = (b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7];
        var chunks = new List<(int comp, int decomp)>();
        int pos;
        if (headerSize == 0) { chunks.Add((b.Length - 8, -1)); pos = 8; }
        else
        {
            int count = (b[9] << 16) | (b[10] << 8) | b[11];
            for (int i = 0; i < count; i++)
            {
                int p = 12 + i * 24;
                chunks.Add(((b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3],
                            (b[p + 4] << 24) | (b[p + 5] << 16) | (b[p + 6] << 8) | b[p + 7]));
            }
            pos = headerSize;
        }
        using var outp = new MemoryStream();
        foreach (var (comp, decomp) in chunks)
        {
            switch ((char)b[pos])
            {
                case 'N': outp.Write(b, pos + 1, comp - 1); break;
                case 'Z':
                    using (var z = new ZLibStream(new MemoryStream(b, pos + 1, comp - 1), CompressionMode.Decompress))
                        z.CopyTo(outp);
                    break;
                case 'E': // encrypted chunk: keys unknown, leave zeros so offsets stay right
                    if (decomp > 0) outp.Write(new byte[decomp]);
                    break;
                default: throw new InvalidDataException($"BLTE mode {(char)b[pos]}");
            }
            pos += comp;
        }
        return outp.ToArray();
    }
}
