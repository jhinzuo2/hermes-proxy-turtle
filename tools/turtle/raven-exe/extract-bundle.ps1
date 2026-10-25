# List a single-file HermesProxy.exe bundle and extract HermesProxy.dll + Framework.dll (for decompiling).
# Usage: .\extract-bundle.ps1 -Exe "<path>\HermesProxy.exe" -OutDir "<dir>"
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$OutDir)
. "$PSScriptRoot\bundle-common.ps1"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$bundle = Read-BundleEntries $Exe
Write-Output "bundle marker at $($bundle.Marker), format v$($bundle.Major), $($bundle.Entries.Count) entries"
$bundle.Entries | ForEach-Object { "{0,-40} type={1} size={2} comp={3}" -f $_.Path, $_.Type, $_.Size, $_.Comp }
foreach ($e in $bundle.Entries | Where-Object { $_.Path -in @('HermesProxy.dll', 'Framework.dll') }) {
    $data = Get-BundleEntryBytes $bundle $e
    [System.IO.File]::WriteAllBytes((Join-Path $OutDir $e.Path), $data)
    Write-Output ("extracted {0} ({1} bytes)" -f $e.Path, $data.Length)
}
$bundle.Reader.Close()
