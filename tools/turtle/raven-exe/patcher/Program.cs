using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

class Program
{
    static int Main(string[] args)
    {
        string inDll = args[0];
        string outDll = args.Length > 1 ? args[1] : null; // if null -> inspect only

        int reportedTok = 0, serverTok = 0;
        using (var fs = File.OpenRead(inDll))
        using (var pe = new PEReader(fs))
        {
            var mr = pe.GetMetadataReader();
            foreach (var tdh in mr.TypeDefinitions)
            {
                var td = mr.GetTypeDefinition(tdh);
                if (mr.GetString(td.Name) != "Settings") continue;
                foreach (var fh in td.GetFields())
                {
                    var fd = mr.GetFieldDefinition(fh);
                    var fname = mr.GetString(fd.Name);
                    int tok = MetadataTokens.GetToken(fh);
                    if (fname == "ReportedBuild") reportedTok = tok;
                    if (fname == "ServerBuild") serverTok = tok;
                }
            }
        }
        Console.WriteLine($"ReportedBuild token = 0x{reportedTok:X8}");
        Console.WriteLine($"ServerBuild   token = 0x{serverTok:X8}");
        if (reportedTok == 0 || serverTok == 0) { Console.WriteLine("ERROR: token not found"); return 2; }

        byte[] rbLE = BitConverter.GetBytes(reportedTok);
        byte[] sbLE = BitConverter.GetBytes(serverTok);

        byte[] bytes = File.ReadAllBytes(inDll);
        // pattern: 0x7E <reportedTok LE 4> 0x28  (ldsfld ReportedBuild ; call WriteUInt32)
        var hits = new System.Collections.Generic.List<int>();
        for (int i = 0; i < bytes.Length - 6; i++)
        {
            if (bytes[i] == 0x7E &&
                bytes[i + 1] == rbLE[0] && bytes[i + 2] == rbLE[1] && bytes[i + 3] == rbLE[2] && bytes[i + 4] == rbLE[3] &&
                bytes[i + 5] == 0x28)
            {
                hits.Add(i);
            }
        }
        Console.WriteLine($"world-site pattern (ldsfld ReportedBuild; call) matches: {hits.Count}");
        foreach (var h in hits) Console.WriteLine($"  at file offset {h}");

        // also count all ldsfld ReportedBuild for sanity
        int allRb = 0;
        for (int i = 0; i < bytes.Length - 5; i++)
            if (bytes[i] == 0x7E && bytes[i+1]==rbLE[0] && bytes[i+2]==rbLE[1] && bytes[i+3]==rbLE[2] && bytes[i+4]==rbLE[3]) allRb++;
        Console.WriteLine($"total ldsfld ReportedBuild occurrences: {allRb}");

        if (outDll == null) { Console.WriteLine("(inspect only, no patch written)"); return 0; }
        if (hits.Count != 1) { Console.WriteLine("ERROR: expected exactly 1 world-site match, aborting patch."); return 3; }

        int off = hits[0];
        // overwrite the 4-byte field token (bytes off+1 .. off+4) with ServerBuild token
        bytes[off + 1] = sbLE[0];
        bytes[off + 2] = sbLE[1];
        bytes[off + 3] = sbLE[2];
        bytes[off + 4] = sbLE[3];
        File.WriteAllBytes(outDll, bytes);
        Console.WriteLine($"PATCHED: wrote ServerBuild token at offset {off+1}; output -> {outDll}");
        Console.WriteLine($"output size = {new FileInfo(outDll).Length} (input = {new FileInfo(inDll).Length})");
        return 0;
    }
}
