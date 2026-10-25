# Shared helpers for reading a .NET single-file bundle (e.g. Ravencraft's shipped HermesProxy.exe).

# The .NET host stores the bundle header offset in the 8 bytes before this marker.
$BundleMarker = [byte[]](0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
                         0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae)

function Find-BundleMarker([byte[]]$bytes) {
    $first = $BundleMarker[0]
    for ($i = [Array]::IndexOf($bytes, $first); $i -ge 0 -and $i -le $bytes.Length - $BundleMarker.Length; $i = [Array]::IndexOf($bytes, $first, $i + 1)) {
        $match = $true
        for ($j = 1; $j -lt $BundleMarker.Length; $j++) { if ($bytes[$i + $j] -ne $BundleMarker[$j]) { $match = $false; break } }
        if ($match) { return $i }
    }
    throw "No .NET bundle marker found - not a single-file bundle?"
}

# Returns the bundle's file entries (Offset, Size, Comp, Type, Path) plus the open reader.
function Read-BundleEntries([string]$exe) {
    $bytes = [System.IO.File]::ReadAllBytes($exe)
    $marker = Find-BundleMarker $bytes
    $fs = New-Object System.IO.MemoryStream(, $bytes)
    $br = New-Object System.IO.BinaryReader($fs)
    $fs.Position = $marker - 8
    $fs.Position = $br.ReadInt64()
    $major = $br.ReadUInt32(); $null = $br.ReadUInt32(); $fileCount = $br.ReadInt32(); $null = $br.ReadString()
    if ($major -ge 2) { $null = $br.ReadInt64(); $null = $br.ReadInt64(); $null = $br.ReadInt64(); $null = $br.ReadInt64(); $null = $br.ReadUInt64() }
    $entries = for ($k = 0; $k -lt $fileCount; $k++) {
        $o = $br.ReadInt64(); $s = $br.ReadInt64(); $cs = 0
        if ($major -ge 6) { $cs = $br.ReadInt64() }
        $type = $br.ReadByte(); $rel = $br.ReadString()
        [pscustomobject]@{ Offset = $o; Size = $s; Comp = $cs; Type = $type; Path = $rel }
    }
    [pscustomobject]@{ Marker = $marker; Major = $major; Entries = $entries; Reader = $br }
}

function Get-BundleEntryBytes($bundle, $entry) {
    $br = $bundle.Reader
    $br.BaseStream.Position = $entry.Offset
    if ($entry.Comp -gt 0) {
        $cin = New-Object System.IO.MemoryStream(, $br.ReadBytes([int]$entry.Comp))
        $ds = New-Object System.IO.Compression.DeflateStream($cin, [System.IO.Compression.CompressionMode]::Decompress)
        $cout = New-Object System.IO.MemoryStream
        $ds.CopyTo($cout); $ds.Close()
        return $cout.ToArray()
    }
    return $br.ReadBytes([int]$entry.Size)
}
