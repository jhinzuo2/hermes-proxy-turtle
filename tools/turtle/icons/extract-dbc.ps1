# Extract DBC files from a Turtle client's MPQs (later archives win), using StormLib.dll (x64).
# Example: .\extract-dbc.ps1 -DataDir "<turtle client>\Data" -StormLib "<path>\StormLib.dll" -OutDir .\dbc
param(
  [Parameter(Mandatory)][string]$DataDir,
  [Parameter(Mandatory)][string]$StormLib,
  [Parameter(Mandatory)][string]$OutDir,
  [string[]]$Files = @('DBFilesClient\ItemDisplayInfo.dbc', 'DBFilesClient\CreatureDisplayInfo.dbc', 'DBFilesClient\GameObjectDisplayInfo.dbc')
)
$lib = (Resolve-Path $StormLib).Path.Replace('\', '\\')
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Storm {
  [DllImport("$lib", CharSet=CharSet.Unicode)]
  public static extern bool SFileOpenArchive(string name, uint prio, uint flags, out IntPtr h);
  [DllImport("$lib")]
  public static extern bool SFileCloseArchive(IntPtr h);
  [DllImport("$lib", CharSet=CharSet.Ansi)]
  public static extern bool SFileHasFile(IntPtr h, string name);
  [DllImport("$lib", CharSet=CharSet.Unicode)]
  public static extern bool SFileExtractFile(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string name, string outName, uint scope);
}
"@
$order = @('base.MPQ', 'dbc.MPQ', 'interface.MPQ', 'misc.MPQ', 'model.MPQ', 'texture.MPQ', 'wmo.MPQ', 'patch.MPQ',
           'patch-2.MPQ', 'patch-3.mpq', 'patch-4.mpq', 'patch-5.mpq', 'patch-6.mpq', 'patch-7.mpq', 'patch-8.mpq', 'patch-9.mpq')
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($a in $order) {
  $p = Join-Path $DataDir $a
  if (-not (Test-Path $p)) { continue }
  $h = [IntPtr]::Zero
  # 0x100 = STREAM_FLAG_READ_ONLY
  if (-not [Storm]::SFileOpenArchive($p, 0, 0x100, [ref]$h)) { Write-Output "open failed: $a"; continue }
  foreach ($f in $Files) {
    if ([Storm]::SFileHasFile($h, $f)) {
      $ok = [Storm]::SFileExtractFile($h, $f, (Join-Path $OutDir (Split-Path $f -Leaf)), 0)
      Write-Output "$a : $f -> $ok"
    }
  }
  [Storm]::SFileCloseArchive($h) | Out-Null
}
